using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Authorization;

// Resolves whether a principal holds `permission` at `scope`, honouring the scope hierarchy
// (tenant ⊇ project ⊇ environment) across both direct grants and role assignments.
public sealed class AuthorizationService(ApplicationDbContext db) : IAuthorizationService
{
    public async Task AuthorizeAsync(Guid principalId, Permission permission, Scope scope, CancellationToken ct = default)
    {
        if (!await IsAuthorizedAsync(principalId, permission, scope, ct))
        {
            throw new ForbiddenException(
                $"Principal {principalId} lacks permission '{PermissionCatalog.ToToken(permission)}' at {scope.Type}.");
        }
    }

    public async Task<bool> IsAuthorizedAsync(
        Guid principalId, Permission permission, Scope scope, CancellationToken ct = default)
    {
        var ancestors = await AncestorScopesAsync(scope, ct);

        // Direct permission grants (tenant filter auto-applies to the current tenant).
        var grants = await db.PermissionGrants
            .Where(g => g.PrincipalId == principalId && g.Permission == permission)
            .ToListAsync(ct);
        if (grants.Any(g => ancestors.Contains(g.Scope)))
        {
            return true;
        }

        // Role assignments whose scope covers the request, expanded to their permissions.
        var assignments = await db.RoleAssignments
            .Where(a => a.PrincipalId == principalId)
            .ToListAsync(ct);
        var roleIds = assignments
            .Where(a => ancestors.Contains(a.Scope))
            .Select(a => a.RoleId)
            .Distinct()
            .ToList();
        if (roleIds.Count == 0)
        {
            return false;
        }

        return await db.RolePermissions
            .AnyAsync(rp => roleIds.Contains(rp.RoleId) && rp.Permission == permission, ct);
    }

    // The set of scopes that "cover" the requested scope: itself plus its ancestors.
    // Environment → its parent project (looked up) → tenant. Project → tenant. Tenant → itself.
    private async Task<HashSet<Scope>> AncestorScopesAsync(Scope scope, CancellationToken ct)
    {
        var set = new HashSet<Scope> { Scope.Tenant };
        switch (scope.Type)
        {
            case ScopeType.Project:
                set.Add(scope);
                break;
            case ScopeType.Environment:
                set.Add(scope);
                var projectId = await db.ProjectEnvironments
                    .Where(e => e.Id == scope.TargetId)
                    .Select(e => (Guid?)e.ProjectId)
                    .FirstOrDefaultAsync(ct);
                if (projectId is Guid pid)
                {
                    set.Add(Scope.Project(pid));
                }
                break;
            case ScopeType.Tenant:
            default:
                break;
        }
        return set;
    }
}
