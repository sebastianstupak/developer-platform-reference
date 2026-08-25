using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Authorization;

// A machine principal within a tenant. API key credentials (Slice 4) authenticate as it.
public class ServiceAccount : TenantEntity
{
    public Guid PrincipalId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private ServiceAccount() { }

    public static ServiceAccount Create(Guid tenantId, Guid principalId, string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ServiceAccount
        {
            TenantId = tenantId,
            PrincipalId = principalId,
            Name = name,
            Description = description
        };
    }
}
