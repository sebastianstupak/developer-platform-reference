using System.Security.Claims;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Authorization;

public sealed record ResolvedPrincipal(Guid PrincipalId, PrincipalType Type, Guid? UserId);

public interface IPrincipalResolver
{
    // Resolves the authenticated caller to a principal in `tenantId`, JIT-creating the User/Membership
    // on first login. Returns null if the token carries no usable subject.
    Task<ResolvedPrincipal?> ResolveAsync(ClaimsPrincipal user, Guid tenantId, CancellationToken ct = default);
}
