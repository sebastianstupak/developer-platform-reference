namespace DeveloperPlatform.Application.Crypto;

public interface ITenantCryptoService
{
    // Returns (encryptedPayload, keyId)
    Task<(byte[] EncryptedPayload, Guid KeyId)> EncryptAsync(Guid tenantId, string plaintext, CancellationToken ct = default);
    Task<string> DecryptAsync(Guid tenantId, byte[] encryptedPayload, Guid keyId, CancellationToken ct = default);
    Task CreateKeyAsync(Guid tenantId, CancellationToken ct = default);
    Task ShredKeyAsync(Guid tenantId, CancellationToken ct = default);
}
