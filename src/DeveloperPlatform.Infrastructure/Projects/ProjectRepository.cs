using DeveloperPlatform.Domain.Projects;
using DeveloperPlatform.Infrastructure.Persistence;

namespace DeveloperPlatform.Infrastructure.Projects;

public sealed class ProjectRepository(ApplicationDbContext db) : IProjectRepository
{
    public async Task AddAsync(Project project, CancellationToken ct = default)
        => await db.Projects.AddAsync(project, ct);

    public void Delete(Project project)
        => db.Projects.Remove(project);
}
