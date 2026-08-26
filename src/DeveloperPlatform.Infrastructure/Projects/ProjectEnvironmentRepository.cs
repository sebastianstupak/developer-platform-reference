using DeveloperPlatform.Domain.Projects;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Projects;

public sealed class ProjectEnvironmentRepository(ApplicationDbContext db) : IProjectEnvironmentRepository
{
    public async Task AddAsync(ProjectEnvironment environment, CancellationToken ct = default)
        => await db.ProjectEnvironments.AddAsync(environment, ct);

    public async Task<ProjectEnvironment?> GetAsync(Guid projectId, Guid environmentId, CancellationToken ct = default)
        => await db.ProjectEnvironments
            .FirstOrDefaultAsync(e => e.ProjectId == projectId && e.Id == environmentId, ct);

    public async Task<IReadOnlyList<ProjectEnvironment>> ListAsync(Guid projectId, CancellationToken ct = default)
        => await db.ProjectEnvironments.AsNoTracking()
            .Where(e => e.ProjectId == projectId)
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

    public void Delete(ProjectEnvironment environment) => db.ProjectEnvironments.Remove(environment);
}
