using DeveloperPlatform.Domain.Projects;

namespace DeveloperPlatform.Infrastructure.Projects;

public interface IProjectRepository
{
    Task AddAsync(Project project, CancellationToken ct = default);
    void Delete(Project project);
}
