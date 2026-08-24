namespace DeveloperPlatform.Domain.Abstractions;

public interface ITenantScoped
{
    Guid TenantId { get; }
}
