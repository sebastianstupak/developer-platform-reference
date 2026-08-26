using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Grants.AssignRole;

[RequiresPermission(Permission.RolesManage)]
public record AssignRoleCommand(Guid PrincipalId, Guid RoleId, ScopeType ScopeType, Guid? ScopeTargetId)
    : ICommand<AssignRoleResult>;

public record AssignRoleResult(Guid AssignmentId);
