using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Members.InviteMember;

[RequiresPermission(Permission.MembersManage)]
public record InviteMemberCommand(string Email, Guid RoleId, ScopeType ScopeType, Guid? ScopeTargetId)
    : ICommand<InviteMemberResult>;

public record InviteMemberResult(Guid InvitationId, string Token);
