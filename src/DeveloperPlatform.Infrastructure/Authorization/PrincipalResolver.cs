using System.Security.Claims;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Authorization;

public sealed class PrincipalResolver(ApplicationDbContext db) : IPrincipalResolver
{
    public async Task<ResolvedPrincipal?> ResolveAsync(
        ClaimsPrincipal user, Guid tenantId, CancellationToken ct = default)
    {
        var subject = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        // Find-or-JIT-create the global User (keyed by Keycloak subject).
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

        // Find the membership for this user in this tenant.
        var membership = await db.Memberships
            .FirstOrDefaultAsync(m => m.UserId == dbUser.Id, ct); // tenant filter scopes to current tenant
        if (membership is not null)
        {
            return new ResolvedPrincipal(membership.PrincipalId, PrincipalType.Member, dbUser.Id);
        }

        // JIT-create a principal + membership. Bootstrap: the first member of a tenant becomes Owner.
        var isFirstMember = !await db.Memberships.AnyAsync(ct); // filtered to this tenant
        var principal = Principal.CreateMember(tenantId, dbUser.DisplayName);
        db.Principals.Add(principal);
        db.Memberships.Add(Membership.Create(tenantId, principal.Id, dbUser.Id, MembershipStatus.Active));
        if (isFirstMember)
        {
            db.RoleAssignments.Add(RoleAssignment.Create(tenantId, principal.Id, SystemRoles.OwnerId, Scope.Tenant));
        }
        await db.SaveChangesAsync(ct);

        return new ResolvedPrincipal(principal.Id, PrincipalType.Member, dbUser.Id);
    }
}
