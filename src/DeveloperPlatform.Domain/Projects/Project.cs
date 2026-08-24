using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Projects;

public class Project : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private Project() { }

    public static Project Create(Guid tenantId, string name, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Project
        {
            TenantId = tenantId,
            Name = name,
            Description = description
        };
    }
}
