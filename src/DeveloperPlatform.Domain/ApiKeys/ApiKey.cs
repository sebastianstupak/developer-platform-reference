using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.ApiKeys;

public class ApiKey : TenantEntity
{
    public Guid ProjectId { get; private set; }
    public Guid? EnvironmentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string KeyPrefix { get; private set; } = string.Empty;   // e.g. "dpk_live_"
    public string KeyHash { get; private set; } = string.Empty;     // bcrypt/SHA-256 hash
    public ApiKeyScope Scopes { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }

    private ApiKey() { }

    public static ApiKey Create(
        Guid tenantId, Guid projectId, Guid? environmentId,
        string name, string keyPrefix, string keyHash,
        ApiKeyScope scopes, DateTime? expiresAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ApiKey
        {
            TenantId = tenantId,
            ProjectId = projectId,
            EnvironmentId = environmentId,
            Name = name,
            KeyPrefix = keyPrefix,
            KeyHash = keyHash,
            Scopes = scopes,
            ExpiresAt = expiresAt
        };
    }

    public void Revoke()
    {
        IsRevoked = true;
        RevokedAt = DateTime.UtcNow;
    }

    public void RecordUsage() => LastUsedAt = DateTime.UtcNow;
}
