using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Members.GetMembers;

[RequiresPermission(Permission.MembersManage)]
public record GetMembersQuery : IQuery<IReadOnlyList<MemberSummary>>;

public record MemberSummary(Guid PrincipalId, Guid UserId, string Email, string DisplayName, string Status);
