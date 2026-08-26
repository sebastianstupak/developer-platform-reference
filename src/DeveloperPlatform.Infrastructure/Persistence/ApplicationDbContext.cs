using System.Reflection;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Abstractions;
using DeveloperPlatform.Domain.ApiKeys;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;
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
    public DbSet<Secret> Secrets => Set<Secret>();
    public DbSet<AuditOutboxEntry> AuditOutboxEntries => Set<AuditOutboxEntry>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Principal> Principals => Set<Principal>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<ServiceAccount> ServiceAccounts => Set<ServiceAccount>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<PermissionGrant> PermissionGrants => Set<PermissionGrant>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<ApiKeyCredential> ApiKeyCredentials => Set<ApiKeyCredential>();

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
