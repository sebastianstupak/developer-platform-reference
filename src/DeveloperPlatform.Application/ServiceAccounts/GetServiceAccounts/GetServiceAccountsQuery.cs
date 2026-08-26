using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.ServiceAccounts.GetServiceAccounts;

[RequiresPermission(Permission.ServiceAccountsManage)]
public record GetServiceAccountsQuery : IQuery<IReadOnlyList<ServiceAccountSummary>>;

public record ServiceAccountSummary(Guid PrincipalId, string Name, string? Description, DateTime CreatedAt);
