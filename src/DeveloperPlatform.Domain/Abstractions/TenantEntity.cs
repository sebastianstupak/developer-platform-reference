namespace DeveloperPlatform.Domain.Abstractions;

public abstract class TenantEntity : IEntity, ITenantScoped
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public Guid TenantId { get; protected set; }

    protected TenantEntity() { }

    protected TenantEntity(Guid tenantId)
    {
        TenantId = tenantId;
    }
}
