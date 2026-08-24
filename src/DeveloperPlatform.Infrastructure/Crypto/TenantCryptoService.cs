using System.Security.Cryptography;
using System.Text;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Domain.Tenants;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Crypto;

// Storage format for encrypted blobs: [nonce(12)][tag(16)][ciphertext(N)]
public sealed class TenantCryptoService(ApplicationDbContext db, byte[] masterKey) : ITenantCryptoService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    public async Task CreateKeyAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenantKey = RandomNumberGenerator.GetBytes(KeySize);
        var encryptedKey = EncryptWithMasterKey(tenantKey);
        Array.Clear(tenantKey);

        var entry = TenantEncryptionKey.Create(tenantId, encryptedKey);
        db.TenantEncryptionKeys.Add(entry);
    }

    public async Task<(byte[] EncryptedPayload, Guid KeyId)> EncryptAsync(
        Guid tenantId, string plaintext, CancellationToken ct = default)
    {
        var keyEntry = await GetActiveKeyAsync(tenantId, ct);
        var tenantKey = DecryptWithMasterKey(keyEntry.EncryptedKey);

        try
        {
            var payload = Encrypt(tenantKey, Encoding.UTF8.GetBytes(plaintext));
            return (payload, keyEntry.Id);
        }
        finally
        {
            Array.Clear(tenantKey);
        }
    }

    public async Task<string> DecryptAsync(
        Guid tenantId, byte[] encryptedPayload, Guid keyId, CancellationToken ct = default)
    {
        var keyEntry = await db.TenantEncryptionKeys
            .FirstOrDefaultAsync(k => k.Id == keyId && k.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Encryption key {keyId} not found.");

        if (keyEntry.IsShredded)
            throw new InvalidOperationException(
                $"Encryption key for tenant {tenantId} has been shredded. Data is unrecoverable.");

        var tenantKey = DecryptWithMasterKey(keyEntry.EncryptedKey);
        try
        {
            return Encoding.UTF8.GetString(Decrypt(tenantKey, encryptedPayload));
        }
        finally
        {
            Array.Clear(tenantKey);
        }
    }

    public async Task ShredKeyAsync(Guid tenantId, CancellationToken ct = default)
    {
        var keys = await db.TenantEncryptionKeys
            .Where(k => k.TenantId == tenantId && k.ShreddedAt == null)
            .ToListAsync(ct);

        foreach (var key in keys)
            key.Shred();
    }

    private async Task<TenantEncryptionKey> GetActiveKeyAsync(Guid tenantId, CancellationToken ct)
    {
        return await db.TenantEncryptionKeys
            .Where(k => k.TenantId == tenantId && k.ShreddedAt == null)
            .OrderByDescending(k => k.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"No active encryption key for tenant {tenantId}.");
    }

    // AES-256-GCM encrypt. Output: [nonce(12)][tag(16)][ciphertext]
    private static byte[] Encrypt(byte[] key, byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);
        return result;
    }

    // AES-256-GCM decrypt. Input: [nonce(12)][tag(16)][ciphertext]
    private static byte[] Decrypt(byte[] key, byte[] blob)
    {
        var nonce = blob[..NonceSize];
        var tag = blob[NonceSize..(NonceSize + TagSize)];
        var ciphertext = blob[(NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    // Envelope: encrypt tenant key with master key using AES-256-GCM
    private byte[] EncryptWithMasterKey(byte[] tenantKey) => Encrypt(masterKey, tenantKey);
    private byte[] DecryptWithMasterKey(byte[] encryptedKey) => Decrypt(masterKey, encryptedKey);
}
