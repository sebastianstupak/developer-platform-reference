using DeveloperPlatform.Domain.Projects;

namespace DeveloperPlatform.Infrastructure.Projects;

public interface IProjectEnvironmentRepository
{
    Task AddAsync(ProjectEnvironment environment, CancellationToken ct = default);
    Task<ProjectEnvironment?> GetAsync(Guid projectId, Guid environmentId, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectEnvironment>> ListAsync(Guid projectId, CancellationToken ct = default);
    void Delete(ProjectEnvironment environment);
}
