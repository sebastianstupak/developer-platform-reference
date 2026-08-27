using DeveloperPlatform.Application.Projects.GetProjects;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Projects;

public sealed class GetProjectsQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetProjectsQuery, IReadOnlyList<ProjectSummary>>
{
    public async Task<IReadOnlyList<ProjectSummary>> HandleAsync(
        GetProjectsQuery query, CancellationToken ct = default)
        => await db.Projects
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProjectSummary(
                p.Id, p.Name, p.Description, p.CreatedAt,
                db.ProjectEnvironments.Count(e => e.ProjectId == p.Id),
                db.Secrets.Where(s => s.ProjectId == p.Id)
                    .Select(s => (DateTime?)s.UpdatedAt).Max() ?? p.CreatedAt))
            .ToListAsync(ct);
}
