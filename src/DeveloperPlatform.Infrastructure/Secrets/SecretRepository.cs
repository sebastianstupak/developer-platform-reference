using DeveloperPlatform.Domain.Secrets;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class SecretRepository(ApplicationDbContext db) : ISecretRepository
{
    public async Task<Secret?> GetAsync(Guid environmentId, string name, CancellationToken ct = default)
        => await db.Secrets.FirstOrDefaultAsync(s => s.EnvironmentId == environmentId && s.Name == name, ct);

    public async Task<IReadOnlyList<Secret>> ListAsync(Guid environmentId, CancellationToken ct = default)
        => await db.Secrets.AsNoTracking()
            .Where(s => s.EnvironmentId == environmentId).OrderBy(s => s.Name).ToListAsync(ct);

    public async Task AddAsync(Secret secret, CancellationToken ct = default) => await db.Secrets.AddAsync(secret, ct);
    public void Delete(Secret secret) => db.Secrets.Remove(secret);
}
