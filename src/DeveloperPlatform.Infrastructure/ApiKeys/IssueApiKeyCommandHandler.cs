using System.Security.Cryptography;
using System.Text;
using DeveloperPlatform.Application.ApiKeys.IssueApiKey;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Domain.ApiKeys;
using DeveloperPlatform.Infrastructure.Persistence;

namespace DeveloperPlatform.Infrastructure.ApiKeys;

public sealed class IssueApiKeyCommandHandler(
    ApplicationDbContext db, IExecutionContext executionContext)
    : ICommandHandler<IssueApiKeyCommand, IssueApiKeyResult>
{
    public async Task<IssueApiKeyResult> HandleAsync(IssueApiKeyCommand command, CancellationToken ct = default)
    {
        var rawBytes = RandomNumberGenerator.GetBytes(32);
        var plaintextKey = "dpk_" + Convert.ToBase64String(rawBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey)));
        var keyPrefix = plaintextKey[..12];   // "dpk_" + 8 chars, shown in listings

        var credential = ApiKeyCredential.Create(
            executionContext.TenantId, command.ServiceAccountId, command.Name,
            keyPrefix, keyHash, command.ExpiresAt);
        db.ApiKeyCredentials.Add(credential);
        await db.SaveChangesAsync(ct);

        return new IssueApiKeyResult(credential.Id, plaintextKey, keyPrefix);
    }
}
