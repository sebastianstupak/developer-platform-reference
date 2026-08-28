using DeveloperPlatform.Domain.Secrets;

namespace DeveloperPlatform.Infrastructure.Secrets;

public interface ISecretRepository
{
    Task<Secret?> GetAsync(Guid environmentId, string name, CancellationToken ct = default);
    Task<IReadOnlyList<Secret>> ListAsync(Guid environmentId, CancellationToken ct = default);
    Task AddAsync(Secret secret, CancellationToken ct = default);
    void Delete(Secret secret);
    Task AddVersionAsync(SecretVersion version, CancellationToken ct = default);
    Task<SecretVersion?> GetVersionAsync(Guid secretId, int versionNumber, CancellationToken ct = default);
    Task RemoveVersionsForSecretAsync(Guid secretId, CancellationToken ct = default);
}
