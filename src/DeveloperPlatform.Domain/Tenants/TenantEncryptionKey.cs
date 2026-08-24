using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Tenants;

public class TenantEncryptionKey : IEntity
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public Guid TenantId { get; private set; }
    public byte[] EncryptedKey { get; private set; } = [];  // AES-256 key, envelope-encrypted
    public DateTime? ShreddedAt { get; private set; }
    public bool IsShredded => ShreddedAt.HasValue;

    private TenantEncryptionKey() { }

    public static TenantEncryptionKey Create(Guid tenantId, byte[] encryptedKey)
    {
        return new TenantEncryptionKey
        {
            TenantId = tenantId,
            EncryptedKey = encryptedKey
        };
    }

    public void Shred()
    {
        Array.Clear(EncryptedKey);
        EncryptedKey = [];
        ShreddedAt = DateTime.UtcNow;
    }
}
