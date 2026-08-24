using DeveloperPlatform.Domain.ApiKeys;
using DeveloperPlatform.Infrastructure.Persistence;

namespace DeveloperPlatform.Infrastructure.ApiKeys;

public sealed class ApiKeyRepository(ApplicationDbContext db) : IApiKeyRepository
{
    public async Task AddAsync(ApiKey apiKey, CancellationToken ct = default)
        => await db.ApiKeys.AddAsync(apiKey, ct);
}
