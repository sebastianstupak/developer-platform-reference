using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Tenants;

public class Tenant : IEntity
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public string Name { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    private Tenant() { }

    public static Tenant Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Tenant { Name = name };
    }

    public void MarkDeleted()
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
    }
}
