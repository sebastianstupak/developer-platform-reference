using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Authorization;

// A direct (ACL) grant of a single permission to a principal at a scope, outside any role.
public class PermissionGrant : TenantEntity
{
    public Guid PrincipalId { get; private set; }
    public Permission Permission { get; private set; }
    public ScopeType ScopeType { get; private set; }
    public Guid? ScopeTargetId { get; private set; }

    public Scope Scope => Scope.Create(ScopeType, ScopeTargetId);

    private PermissionGrant() { }

    public static PermissionGrant Create(Guid tenantId, Guid principalId, Permission permission, Scope scope) =>
        new()
        {
            TenantId = tenantId,
            PrincipalId = principalId,
            Permission = permission,
            ScopeType = scope.Type,
            ScopeTargetId = scope.TargetId
        };
}
