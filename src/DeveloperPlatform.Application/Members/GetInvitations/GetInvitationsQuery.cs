using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Members.GetInvitations;

[RequiresPermission(Permission.MembersManage)]
public record GetInvitationsQuery : IQuery<IReadOnlyList<InvitationSummary>>;

public record InvitationSummary(Guid Id, string Email, Guid RoleId, string Status, DateTime ExpiresAt);
