using DeveloperPlatform.Domain.Secrets;

namespace DeveloperPlatform.Infrastructure.Secrets;

public interface ISecretRepository
{
    Task<Secret?> GetAsync(Guid environmentId, string name, CancellationToken ct = default);
    Task<IReadOnlyList<Secret>> ListAsync(Guid environmentId, CancellationToken ct = default);
    Task AddAsync(Secret secret, CancellationToken ct = default);
    void Delete(Secret secret);
}
