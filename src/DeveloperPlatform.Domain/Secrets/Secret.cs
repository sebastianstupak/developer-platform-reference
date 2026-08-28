using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Secrets;

public class Secret : TenantEntity
{
    public Guid ProjectId { get; private set; }
    public Guid EnvironmentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public byte[] EncryptedValue { get; private set; } = [];
    public Guid KeyId { get; private set; }   // which TenantEncryptionKey encrypted the current value
    public int CurrentVersion { get; private set; }   // 1-based; number of the latest SecretVersion
    public DateTime UpdatedAt { get; private set; }

    private Secret() { }

    public static Secret Create(
        Guid tenantId, Guid projectId, Guid environmentId,
        string name, byte[] encryptedValue, Guid keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Secret
        {
            TenantId = tenantId,
            ProjectId = projectId,
            EnvironmentId = environmentId,
            Name = name,
            EncryptedValue = encryptedValue,
            KeyId = keyId,
            CurrentVersion = 1,
            UpdatedAt = DateTime.UtcNow
        };
    }

    // A new value: advances the version counter. Used by set and rollback.
    public void SetNewVersion(byte[] encryptedValue, Guid keyId)
    {
        EncryptedValue = encryptedValue;
        KeyId = keyId;
        CurrentVersion++;
        UpdatedAt = DateTime.UtcNow;
    }

    // Same value re-encrypted under a new key (key rotation): the version does NOT change.
    public void ReEncryptCurrent(byte[] encryptedValue, Guid keyId)
    {
        EncryptedValue = encryptedValue;
        KeyId = keyId;
        UpdatedAt = DateTime.UtcNow;
    }
}
