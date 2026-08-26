using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Grants.RevokePermissionGrant;

[RequiresPermission(Permission.RolesManage)]
public record RevokePermissionGrantCommand(Guid GrantId) : ICommand;
