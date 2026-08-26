using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.ServiceAccounts.CreateServiceAccount;

[RequiresPermission(Permission.ServiceAccountsManage)]
public record CreateServiceAccountCommand(
    string Name, string? Description, IReadOnlyList<GrantSpec> Grants)
    : ICommand<CreateServiceAccountResult>;

public record CreateServiceAccountResult(Guid ServiceAccountId);
