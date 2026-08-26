using DeveloperPlatform.Application.Grants.GetRoles;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class GetRolesQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetRolesQuery, IReadOnlyList<RoleSummary>>
{
    public async Task<IReadOnlyList<RoleSummary>> HandleAsync(GetRolesQuery query, CancellationToken ct = default)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        var perms = await db.RolePermissions.AsNoTracking().ToListAsync(ct);
        return roles.Select(r => new RoleSummary(
            r.Id, r.Name,
            perms.Where(p => p.RoleId == r.Id).Select(p => PermissionCatalog.ToToken(p.Permission)).OrderBy(t => t).ToList()))
            .ToList();
    }
}
