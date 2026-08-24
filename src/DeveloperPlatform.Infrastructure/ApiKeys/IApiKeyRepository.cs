using DeveloperPlatform.Domain.ApiKeys;

namespace DeveloperPlatform.Infrastructure.ApiKeys;

public interface IApiKeyRepository
{
    Task AddAsync(ApiKey apiKey, CancellationToken ct = default);
}
