using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Authorization;

// A named bundle of permissions. v1 ships system roles only (IsSystem = true), which are global
// (not tenant-scoped) and seeded. Tenant-custom roles are a later slice.
public class Role : IEntity
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public string Name { get; private set; } = string.Empty;
    public bool IsSystem { get; private set; }

    private Role() { }

    // Explicit id + createdAt so system roles can be seeded deterministically via HasData.
    public static Role CreateSystem(Guid id, string name, DateTime createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Role { Id = id, Name = name, IsSystem = true, CreatedAt = createdAt };
    }
}
