using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Secrets;

// One immutable entry in a secret's append-only history. Each version keeps the
// key that encrypted it, so retained keys decrypt any version after rotation.
public class SecretVersion : TenantEntity
{
    public Guid SecretId { get; private set; }
    public int VersionNumber { get; private set; }       // 1-based, monotonic per secret
    public byte[] EncryptedValue { get; private set; } = [];
    public Guid KeyId { get; private set; }
    public int? RolledBackFrom { get; private set; }     // set when produced by a rollback

    // Who wrote this version (mirrors the audit trail's principal columns).
    public Guid? CreatedByPrincipalId { get; private set; }
    public string? CreatedByPrincipalType { get; private set; }  // "Member" | "ServiceAccount"
    public Guid? CreatedByUserId { get; private set; }           // the human behind a Member

    private SecretVersion() { }

    public static SecretVersion Create(
        Guid tenantId, Guid secretId, int versionNumber,
        byte[] encryptedValue, Guid keyId,
        Guid? principalId, string? principalType, Guid? userId,
        int? rolledBackFrom = null) => new()
        {
            TenantId = tenantId,
            SecretId = secretId,
            VersionNumber = versionNumber,
            EncryptedValue = encryptedValue,
            KeyId = keyId,
            CreatedByPrincipalId = principalId,
            CreatedByPrincipalType = principalType,
            CreatedByUserId = userId,
            RolledBackFrom = rolledBackFrom
        };
}
