using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Secrets;

public class Secret : TenantEntity
{
    public Guid ProjectId { get; private set; }
    public Guid EnvironmentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public byte[] EncryptedValue { get; private set; } = [];
    public Guid KeyId { get; private set; }   // which TenantEncryptionKey encrypted this
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
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void UpdateValue(byte[] encryptedValue, Guid keyId)
    {
        EncryptedValue = encryptedValue;
        KeyId = keyId;
        UpdatedAt = DateTime.UtcNow;
    }
}
