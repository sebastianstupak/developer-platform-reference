using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Authorization;

// Prevents privilege escalation: an actor may only grant/assign what it itself holds.
public interface IPrivilegeGuard
{
    // Throws ForbiddenException unless the actor holds `permission` at `scope`.
    Task EnsureCanGrantAsync(Guid actorPrincipalId, Permission permission, Scope scope, CancellationToken ct = default);

    // Throws ForbiddenException unless the actor holds EVERY permission of `roleId` at `scope`.
    Task EnsureCanAssignRoleAsync(Guid actorPrincipalId, Guid roleId, Scope scope, CancellationToken ct = default);
}
