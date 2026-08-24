using DeveloperPlatform.Application.Tenancy;

namespace DeveloperPlatform.Infrastructure.Tenancy;

// Mode A: all tenants share the same connection string
public sealed class SharedTablesTenantConnectionResolver(string connectionString)
    : ITenantConnectionResolver
{
    public string Resolve(Guid tenantId) => connectionString;
}
