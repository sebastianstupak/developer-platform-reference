using System.Reflection;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Abstractions;
using DeveloperPlatform.Domain.ApiKeys;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Domain.Projects;
using DeveloperPlatform.Domain.Secrets;
using DeveloperPlatform.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Persistence;

public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    IExecutionContext executionContext,
    TenancyMode tenancyMode) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantEncryptionKey> TenantEncryptionKeys => Set<TenantEncryptionKey>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectEnvironment> ProjectEnvironments => Set<ProjectEnvironment>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Secret> Secrets => Set<Secret>();
    public DbSet<AuditOutboxEntry> AuditOutboxEntries => Set<AuditOutboxEntry>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Auto-apply tenant filter to all ITenantScoped entities (Mode A only)
        if (tenancyMode == TenancyMode.SharedTables)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                .Where(t => typeof(ITenantScoped).IsAssignableFrom(t.ClrType) && !t.ClrType.IsAbstract))
            {
                typeof(ApplicationDbContext)
                    .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [modelBuilder]);
            }
        }
    }

    // Captured via closure — EF Core evaluates this per-query, not at model-build time
    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            executionContext.IsCrossTenantOperation ||
            e.TenantId == executionContext.TenantId);
    }
}
