using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Authorization;

public sealed class PrivilegeGuard(IAuthorizationService authorizationService, ApplicationDbContext db)
    : IPrivilegeGuard
{
    public async Task EnsureCanGrantAsync(
        Guid actorPrincipalId, Permission permission, Scope scope, CancellationToken ct = default)
    {
        if (!await authorizationService.IsAuthorizedAsync(actorPrincipalId, permission, scope, ct))
        {
            throw new ForbiddenException(
                $"Cannot grant '{PermissionCatalog.ToToken(permission)}' — the actor does not hold it at {scope.Type}.");
        }
    }

    public async Task EnsureCanAssignRoleAsync(
        Guid actorPrincipalId, Guid roleId, Scope scope, CancellationToken ct = default)
    {
        var rolePermissions = await db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission)
            .ToListAsync(ct);

        foreach (var permission in rolePermissions)
        {
            await EnsureCanGrantAsync(actorPrincipalId, permission, scope, ct);
        }
    }
}
