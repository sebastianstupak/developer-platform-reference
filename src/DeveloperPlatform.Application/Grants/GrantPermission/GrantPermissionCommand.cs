using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Grants.GrantPermission;

[RequiresPermission(Permission.RolesManage)]
public record GrantPermissionCommand(Guid PrincipalId, Permission Permission, ScopeType ScopeType, Guid? ScopeTargetId)
    : ICommand<GrantPermissionResult>;

public record GrantPermissionResult(Guid GrantId);
