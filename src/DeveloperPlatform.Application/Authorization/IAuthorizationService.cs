using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Authorization;

public interface IAuthorizationService
{
    Task<bool> IsAuthorizedAsync(Guid principalId, Permission permission, Scope scope, CancellationToken ct = default);

    // Throws ForbiddenException when the principal is not authorized.
    Task AuthorizeAsync(Guid principalId, Permission permission, Scope scope, CancellationToken ct = default);
}
