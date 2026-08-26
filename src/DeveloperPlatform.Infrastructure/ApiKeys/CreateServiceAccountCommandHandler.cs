using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.ServiceAccounts.CreateServiceAccount;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;

namespace DeveloperPlatform.Infrastructure.ApiKeys;

public sealed class CreateServiceAccountCommandHandler(
    ApplicationDbContext db, IExecutionContext executionContext, IPrivilegeGuard guard)
    : ICommandHandler<CreateServiceAccountCommand, CreateServiceAccountResult>
{
    public async Task<CreateServiceAccountResult> HandleAsync(
        CreateServiceAccountCommand command, CancellationToken ct = default)
    {
        var tenantId = executionContext.TenantId;
        var actor = executionContext.PrincipalId
            ?? throw new DeveloperPlatform.Application.Authorization.ForbiddenException("No acting principal.");
        foreach (var g in command.Grants)
        {
            await guard.EnsureCanGrantAsync(actor, g.Permission, Scope.Create(g.ScopeType, g.ScopeTargetId), ct);
        }

        var principal = Principal.CreateServiceAccount(tenantId, command.Name);
        db.Principals.Add(principal);
        db.ServiceAccounts.Add(ServiceAccount.Create(tenantId, principal.Id, command.Name, command.Description));

        foreach (var g in command.Grants)
        {
            var scope = Scope.Create(g.ScopeType, g.ScopeTargetId);
            db.PermissionGrants.Add(PermissionGrant.Create(tenantId, principal.Id, g.Permission, scope));
        }

        await db.SaveChangesAsync(ct);
        return new CreateServiceAccountResult(principal.Id);
    }
}
