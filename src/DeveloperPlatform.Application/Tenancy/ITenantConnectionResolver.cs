namespace DeveloperPlatform.Application.Tenancy;

public interface ITenantConnectionResolver
{
    string Resolve(Guid tenantId);
}
