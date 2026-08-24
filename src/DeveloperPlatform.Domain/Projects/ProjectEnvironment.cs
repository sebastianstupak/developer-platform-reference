using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Projects;

public enum EnvironmentType { Development, Staging, Production }

public class ProjectEnvironment : TenantEntity
{
    public Guid ProjectId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public EnvironmentType Type { get; private set; }

    private ProjectEnvironment() { }

    public static ProjectEnvironment Create(Guid tenantId, Guid projectId, string name, EnvironmentType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ProjectEnvironment
        {
            TenantId = tenantId,
            ProjectId = projectId,
            Name = name,
            Type = type
        };
    }
}
