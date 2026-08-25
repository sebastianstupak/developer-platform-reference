using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Authorization;

// A pending invite to join a tenant with a role at a scope. Activated on the invitee's first login (Slice 5).
public class Invitation : TenantEntity
{
    public string Email { get; private set; } = string.Empty;
    public Guid RoleId { get; private set; }
    public ScopeType ScopeType { get; private set; }
    public Guid? ScopeTargetId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public InvitationStatus Status { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    public Scope Scope => Scope.Create(ScopeType, ScopeTargetId);

    private Invitation() { }

    public static Invitation Create(
        Guid tenantId, string email, Guid roleId, Scope scope, string token, DateTime expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new Invitation
        {
            TenantId = tenantId,
            Email = email,
            RoleId = roleId,
            ScopeType = scope.Type,
            ScopeTargetId = scope.TargetId,
            Token = token,
            Status = InvitationStatus.Pending,
            ExpiresAt = expiresAt
        };
    }

    public void Accept() => Status = InvitationStatus.Accepted;
    public void Revoke() => Status = InvitationStatus.Revoked;
    public void MarkExpired() => Status = InvitationStatus.Expired;
}
