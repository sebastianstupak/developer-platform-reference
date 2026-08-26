using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Grants.AssignRole;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class AssignRoleCommandHandler(
    ApplicationDbContext db, IExecutionContext executionContext, IPrivilegeGuard guard)
    : ICommandHandler<AssignRoleCommand, AssignRoleResult>
{
    public async Task<AssignRoleResult> HandleAsync(AssignRoleCommand command, CancellationToken ct = default)
    {
        var scope = Scope.Create(command.ScopeType, command.ScopeTargetId);
        var actor = executionContext.PrincipalId
            ?? throw new ForbiddenException("No acting principal.");
        await guard.EnsureCanAssignRoleAsync(actor, command.RoleId, scope, ct);

        var assignment = RoleAssignment.Create(executionContext.TenantId, command.PrincipalId, command.RoleId, scope);
        db.RoleAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);
        return new AssignRoleResult(assignment.Id);
    }
}
