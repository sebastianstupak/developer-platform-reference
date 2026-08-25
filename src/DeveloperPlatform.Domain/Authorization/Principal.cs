using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Authorization;

// The unit that holds grants and is named in the audit trail. A Member or a ServiceAccount is a Principal.
// Membership/ServiceAccount each reference one Principal; grants FK to Principal.Id.
public class Principal : TenantEntity
{
    public string DisplayName { get; private set; } = string.Empty;
    public PrincipalType Type { get; private set; }

    private Principal() { }

    public static Principal CreateMember(Guid tenantId, string displayName) =>
        Create(tenantId, displayName, PrincipalType.Member);

    public static Principal CreateServiceAccount(Guid tenantId, string displayName) =>
        Create(tenantId, displayName, PrincipalType.ServiceAccount);

    private static Principal Create(Guid tenantId, string displayName, PrincipalType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return new Principal { TenantId = tenantId, DisplayName = displayName, Type = type };
    }

    public void Rename(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
    }
}
