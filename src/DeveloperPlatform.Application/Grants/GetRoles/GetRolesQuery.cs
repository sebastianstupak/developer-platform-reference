using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Grants.GetRoles;

[RequiresPermission(Permission.RolesManage)]
public record GetRolesQuery : IQuery<IReadOnlyList<RoleSummary>>;

public record RoleSummary(Guid Id, string Name, IReadOnlyList<string> Permissions);
