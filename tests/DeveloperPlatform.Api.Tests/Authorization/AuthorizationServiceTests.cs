using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Projects;
using DeveloperPlatform.Infrastructure.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class AuthorizationServiceTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private AuthorizationService _sut = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _principal = Guid.NewGuid();
    private readonly Guid _project = Guid.NewGuid();
    private readonly Guid _env = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var ctx = new TestExecutionContext { TenantId = _tenant };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
        // an environment under the project (for cascade resolution)
        _db.ProjectEnvironments.Add(ProjectEnvironment.Create(_tenant, _project, "prod", EnvironmentType.Production));
        // give THIS env a known id by re-fetching is unnecessary; instead add a grant keyed to a real env id:
        await _db.SaveChangesAsync();
        _sut = new AuthorizationService(_db);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Direct_Grant_At_Tenant_Scope_Allows()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsRead, Scope.Tenant));
        await _db.SaveChangesAsync();

        Assert.True(await _sut.IsAuthorizedAsync(_principal, Permission.SecretsRead, Scope.Tenant));
        // tenant grant cascades down to a project-scoped request
        Assert.True(await _sut.IsAuthorizedAsync(_principal, Permission.SecretsRead, Scope.Project(_project)));
    }

    [Fact]
    public async Task Project_Grant_Does_Not_Satisfy_Other_Project()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.ProjectsWrite, Scope.Project(_project)));
        await _db.SaveChangesAsync();

        Assert.True(await _sut.IsAuthorizedAsync(_principal, Permission.ProjectsWrite, Scope.Project(_project)));
        Assert.False(await _sut.IsAuthorizedAsync(_principal, Permission.ProjectsWrite, Scope.Project(Guid.NewGuid())));
        Assert.False(await _sut.IsAuthorizedAsync(_principal, Permission.ProjectsWrite, Scope.Tenant));
    }

    [Fact]
    public async Task Project_Grant_Cascades_To_Its_Environment()
    {
        var envEntity = ProjectEnvironment.Create(_tenant, _project, "staging", EnvironmentType.Staging);
        _db.ProjectEnvironments.Add(envEntity);
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsWrite, Scope.Project(_project)));
        await _db.SaveChangesAsync();

        Assert.True(await _sut.IsAuthorizedAsync(_principal, Permission.SecretsWrite, Scope.Environment(envEntity.Id)));
    }

    [Fact]
    public async Task Role_Assignment_Grants_Its_Permissions()
    {
        var roleId = Guid.NewGuid();
        _db.Roles.Add(Role.CreateSystem(roleId, "TestRole", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        _db.RolePermissions.Add(RolePermission.Create(roleId, Permission.AuditRead));
        _db.RoleAssignments.Add(RoleAssignment.Create(_tenant, _principal, roleId, Scope.Tenant));
        await _db.SaveChangesAsync();

        Assert.True(await _sut.IsAuthorizedAsync(_principal, Permission.AuditRead, Scope.Tenant));
        Assert.False(await _sut.IsAuthorizedAsync(_principal, Permission.SecretsWrite, Scope.Tenant));
    }

    [Fact]
    public async Task Unknown_Principal_Is_Denied_And_Authorize_Throws()
    {
        Assert.False(await _sut.IsAuthorizedAsync(Guid.NewGuid(), Permission.ProjectsRead, Scope.Tenant));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.AuthorizeAsync(Guid.NewGuid(), Permission.ProjectsRead, Scope.Tenant));
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        public Guid TenantId { get; set; }
        public Guid? UserId => null;
        public Guid? ApiKeyId => null;
        public Guid? ProjectId => null;
        public Guid? EnvironmentId => null;
        public string IpAddress => "127.0.0.1";
        public bool IsCrossTenantOperation { get; set; }
    }
}
