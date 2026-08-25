using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Authorization;

// Assigns a role to a principal at a scope. Scope persists as two columns; Scope value object is derived.
public class RoleAssignment : TenantEntity
{
    public Guid PrincipalId { get; private set; }
    public Guid RoleId { get; private set; }
    public ScopeType ScopeType { get; private set; }
    public Guid? ScopeTargetId { get; private set; }

    public Scope Scope => Scope.Create(ScopeType, ScopeTargetId);

    private RoleAssignment() { }

    public static RoleAssignment Create(Guid tenantId, Guid principalId, Guid roleId, Scope scope) =>
        new()
        {
            TenantId = tenantId,
            PrincipalId = principalId,
            RoleId = roleId,
            ScopeType = scope.Type,
            ScopeTargetId = scope.TargetId
        };
}
