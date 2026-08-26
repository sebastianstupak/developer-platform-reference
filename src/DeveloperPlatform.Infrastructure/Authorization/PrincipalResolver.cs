using System.Security.Claims;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Authorization;

public sealed class PrincipalResolver(ApplicationDbContext db, ITenantCryptoService cryptoService)
    : IPrincipalResolver
{
    public async Task<ResolvedPrincipal?> ResolveAsync(
        ClaimsPrincipal user, Guid tenantId, CancellationToken ct = default)
    {
        var subject = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var dbUser = await db.Users.FirstOrDefaultAsync(u => u.KeycloakSubject == subject, ct);
        if (dbUser is null)
        {
            var email = user.FindFirst("email")?.Value ?? $"{subject}@unknown";
            var displayName = user.FindFirst("preferred_username")?.Value
                ?? user.FindFirst("name")?.Value ?? email;
            dbUser = User.Create(subject, email, displayName);
            db.Users.Add(dbUser);
            await db.SaveChangesAsync(ct);
        }

        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == dbUser.Id, ct);
        if (membership is not null)
        {
            return new ResolvedPrincipal(membership.PrincipalId, PrincipalType.Member, dbUser.Id);
        }

        // First member of the tenant → Owner, and provision the tenant encryption key.
        if (!await db.Memberships.AnyAsync(ct))
        {
            var owner = Principal.CreateMember(tenantId, dbUser.DisplayName);
            db.Principals.Add(owner);
            db.Memberships.Add(Membership.Create(tenantId, owner.Id, dbUser.Id, MembershipStatus.Active));
            db.RoleAssignments.Add(RoleAssignment.Create(tenantId, owner.Id, SystemRoles.OwnerId, Scope.Tenant));
            await cryptoService.CreateKeyAsync(tenantId, ct);   // adds a TenantEncryptionKey to the context
            await db.SaveChangesAsync(ct);
            return new ResolvedPrincipal(owner.Id, PrincipalType.Member, dbUser.Id);
        }

        // Otherwise require a matching pending invitation (invitation-gated onboarding).
        var invitation = await db.Invitations.FirstOrDefaultAsync(
            i => i.Email == dbUser.Email && i.Status == InvitationStatus.Pending && i.ExpiresAt > DateTime.UtcNow, ct);
        if (invitation is null)
        {
            return null;   // not a member, no invitation → 403 downstream
        }

        var principal = Principal.CreateMember(tenantId, dbUser.DisplayName);
        db.Principals.Add(principal);
        db.Memberships.Add(Membership.Create(tenantId, principal.Id, dbUser.Id, MembershipStatus.Active));
        db.RoleAssignments.Add(RoleAssignment.Create(tenantId, principal.Id, invitation.RoleId, invitation.Scope));
        invitation.Accept();
        await db.SaveChangesAsync(ct);
        return new ResolvedPrincipal(principal.Id, PrincipalType.Member, dbUser.Id);
    }
}
