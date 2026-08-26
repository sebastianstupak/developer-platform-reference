using System.Security.Cryptography;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Members.InviteMember;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class InviteMemberCommandHandler(
    ApplicationDbContext db, IExecutionContext executionContext, IPrivilegeGuard guard)
    : ICommandHandler<InviteMemberCommand, InviteMemberResult>
{
    public async Task<InviteMemberResult> HandleAsync(InviteMemberCommand command, CancellationToken ct = default)
    {
        var scope = Scope.Create(command.ScopeType, command.ScopeTargetId);
        var actor = executionContext.PrincipalId ?? throw new ForbiddenException("No acting principal.");
        // You can only invite someone to a role you could grant yourself.
        await guard.EnsureCanAssignRoleAsync(actor, command.RoleId, scope, ct);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var invitation = Invitation.Create(
            executionContext.TenantId, command.Email, command.RoleId, scope, token, DateTime.UtcNow.AddDays(7));
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        return new InviteMemberResult(invitation.Id, token);
    }
}
