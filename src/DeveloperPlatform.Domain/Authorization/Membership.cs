using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Authorization;

// A human principal within a tenant: links a global User to a Principal.
public class Membership : TenantEntity
{
    public Guid PrincipalId { get; private set; }
    public Guid UserId { get; private set; }
    public MembershipStatus Status { get; private set; }

    private Membership() { }

    public static Membership Create(Guid tenantId, Guid principalId, Guid userId, MembershipStatus status)
    {
        return new Membership
        {
            TenantId = tenantId,
            PrincipalId = principalId,
            UserId = userId,
            Status = status
        };
    }

    public void Activate() => Status = MembershipStatus.Active;
    public void Suspend() => Status = MembershipStatus.Suspended;
}
