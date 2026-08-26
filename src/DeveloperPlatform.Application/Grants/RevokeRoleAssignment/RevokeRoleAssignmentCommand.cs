using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Grants.RevokeRoleAssignment;

[RequiresPermission(Permission.RolesManage)]
public record RevokeRoleAssignmentCommand(Guid AssignmentId) : ICommand;
