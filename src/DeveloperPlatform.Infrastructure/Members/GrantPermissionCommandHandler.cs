using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Grants.GrantPermission;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class GrantPermissionCommandHandler(
    ApplicationDbContext db, IExecutionContext executionContext, IPrivilegeGuard guard)
    : ICommandHandler<GrantPermissionCommand, GrantPermissionResult>
{
    public async Task<GrantPermissionResult> HandleAsync(GrantPermissionCommand command, CancellationToken ct = default)
    {
        var scope = Scope.Create(command.ScopeType, command.ScopeTargetId);
        var actor = executionContext.PrincipalId
            ?? throw new ForbiddenException("No acting principal.");
        await guard.EnsureCanGrantAsync(actor, command.Permission, scope, ct);

        var grant = PermissionGrant.Create(executionContext.TenantId, command.PrincipalId, command.Permission, scope);
        db.PermissionGrants.Add(grant);
        await db.SaveChangesAsync(ct);
        return new GrantPermissionResult(grant.Id);
    }
}
