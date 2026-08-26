using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.ApiKeys.GetApiKeys;

[RequiresPermission(Permission.ApiKeysManage)]
public record GetApiKeysQuery(Guid ServiceAccountId) : IQuery<IReadOnlyList<ApiKeySummary>>;

public record ApiKeySummary(
    Guid Id, string Name, string KeyPrefix, DateTime? ExpiresAt,
    bool IsRevoked, DateTime? LastUsedAt, DateTime CreatedAt);
