using System.Reflection;
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Audit;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Infrastructure.Dispatching;

public sealed class CommandDispatcher(
    IServiceProvider serviceProvider,
    ApplicationDbContext db,
    IExecutionContext executionContext,
    ITenantCryptoService cryptoService,
    IAuditOutboxRepository auditOutboxRepository,
    SensitiveDataScrubber scrubber,
    TenancyMode tenancyMode,
    IAuthorizationService authorizationService) : ICommandDispatcher
{
    public async Task<TResult> SendAsync<TCommand, TResult>(
        TCommand command, CancellationToken ct = default)
        where TCommand : ICommand<TResult>
    {
        var handler = serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>();
        var skipAudit = typeof(TCommand).GetCustomAttribute<SkipAuditAttribute>() is not null;
        var crossTenant = typeof(TCommand).GetCustomAttribute<CrossTenantAttribute>();

        if (crossTenant is not null)
        {
            if (tenancyMode == TenancyMode.DatabasePerTenant)
            {
                throw new NotSupportedException(
                    "Cross-tenant operations are not supported in DatabasePerTenant mode.");
            }

            executionContext.IsCrossTenantOperation = true;
        }

        var requiresPermission = typeof(TCommand).GetCustomAttribute<RequiresPermissionAttribute>();
        if (requiresPermission is not null)
        {
            if (executionContext.PrincipalId is not Guid principalId)
            {
                throw new ForbiddenException("No principal in the execution context.");
            }

            var scope = command is IResourceScoped scoped
                ? scoped.ResourceScope
                : executionContext.EnvironmentId is Guid envId
                    ? Scope.Environment(envId)
                    : executionContext.ProjectId is Guid projId
                        ? Scope.Project(projId)
                        : Scope.Tenant;

            await authorizationService.AuthorizeAsync(principalId, requiresPermission.Permission, scope, ct);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        try
        {
            var result = await handler.HandleAsync(command, ct);

            if (!skipAudit)
            {
                var entry = await BuildOutboxEntryAsync<TCommand, TResult>(command, AuditStatus.Success, crossTenant, ct);
                await auditOutboxRepository.AddAsync(entry, ct);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct);

            if (!skipAudit)
            {
                await WriteFailedAuditAsync<TCommand, TResult>(command, crossTenant, ct);
            }

            throw;
        }
        finally
        {
            executionContext.IsCrossTenantOperation = false;
        }
    }

    private async Task WriteFailedAuditAsync<TCommand, TResult>(
        TCommand command, CrossTenantAttribute? crossTenant, CancellationToken ct)
        where TCommand : ICommand<TResult>
    {
        try
        {
            // Discard the failed handler's tracked (Modified/Added) entity changes so the
            // rollback below only removes what the transaction rollback already undid in the
            // database — SaveChangesAsync must flush ONLY the freshly-built audit entry, never
            // the failed handler's still-tracked mutations.
            db.ChangeTracker.Clear();

            await using var failTx = await db.Database.BeginTransactionAsync(CancellationToken.None);
            var entry = await BuildOutboxEntryAsync<TCommand, TResult>(command, AuditStatus.Failed, crossTenant, CancellationToken.None);
            await auditOutboxRepository.AddAsync(entry, CancellationToken.None);
            await db.SaveChangesAsync(CancellationToken.None);
            await failTx.CommitAsync(CancellationToken.None);
        }
        catch
        {
            // Best-effort — swallow exceptions if failed audit write fails
        }
    }

    private async Task<AuditOutboxEntry> BuildOutboxEntryAsync<TCommand, TResult>(
        TCommand command, AuditStatus status, CrossTenantAttribute? crossTenant, CancellationToken ct)
        where TCommand : ICommand<TResult>
    {
        var scrubbed = scrubber.ScrubAndSerialize(command);
        var (encrypted, keyId) = await cryptoService.EncryptAsync(executionContext.TenantId, scrubbed, ct);

        return AuditOutboxEntry.Create(
            tenantId: executionContext.TenantId,
            commandType: typeof(TCommand).Name,
            status: status,
            principalId: executionContext.PrincipalId,
            principalType: executionContext.PrincipalType?.ToString(),
            userId: executionContext.UserId,
            projectId: executionContext.ProjectId,
            environmentId: executionContext.EnvironmentId,
            ipAddress: executionContext.IpAddress,
            isCrossTenant: crossTenant is not null,
            crossTenantReason: crossTenant?.Reason,
            encryptedPayload: encrypted,
            keyId: keyId);
    }
}
