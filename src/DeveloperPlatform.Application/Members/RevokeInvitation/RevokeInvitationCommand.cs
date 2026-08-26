using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Members.RevokeInvitation;

[RequiresPermission(Permission.MembersManage)]
public record RevokeInvitationCommand(Guid InvitationId) : ICommand;
