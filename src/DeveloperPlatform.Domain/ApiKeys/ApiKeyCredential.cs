using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.ApiKeys;

// A rotatable API-key credential that authenticates AS a ServiceAccount principal.
// Only a SHA-256 hash of the key is stored; the plaintext is shown once at creation.
public class ApiKeyCredential : TenantEntity
{
    public Guid ServiceAccountId { get; private set; }   // -> Principal.Id (Type = ServiceAccount)
    public string Name { get; private set; } = string.Empty;
    public string KeyPrefix { get; private set; } = string.Empty;   // "dpk_" + first 8 secret chars, shown in listings
    public string KeyHash { get; private set; } = string.Empty;     // SHA-256 hex of the full key
    public DateTime? ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }

    private ApiKeyCredential() { }

    public static ApiKeyCredential Create(
        Guid tenantId, Guid serviceAccountId, string name, string keyPrefix, string keyHash, DateTime? expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyHash);
        return new ApiKeyCredential
        {
            TenantId = tenantId,
            ServiceAccountId = serviceAccountId,
            Name = name,
            KeyPrefix = keyPrefix,
            KeyHash = keyHash,
            ExpiresAt = expiresAt
        };
    }

    public bool IsActive(DateTime nowUtc) =>
        !IsRevoked && (ExpiresAt is null || ExpiresAt > nowUtc);

    public void Revoke()
    {
        IsRevoked = true;
        RevokedAt = DateTime.UtcNow;
    }

    public void RecordUsage() => LastUsedAt = DateTime.UtcNow;
}
