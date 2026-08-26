using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Projects;
using DeveloperPlatform.Infrastructure.Persistence;
using DeveloperPlatform.Infrastructure.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DeveloperPlatform.Api.Tests.Secrets;

public class EnvironmentTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _project = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var ctx = new TestExecutionContext { TenantId = _tenant };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Repository_Adds_And_Lists_By_Project()
    {
        var repo = new ProjectEnvironmentRepository(_db);
        await repo.AddAsync(ProjectEnvironment.Create(_tenant, _project, "Production", EnvironmentType.Production));
        await _db.SaveChangesAsync();

        var list = await repo.ListAsync(_project);
        Assert.Single(list);
        Assert.Equal("Production", list[0].Name);
    }

    [Fact]
    public void Rename_Rejects_Blank()
    {
        var env = ProjectEnvironment.Create(_tenant, _project, "Dev", EnvironmentType.Development);
        Assert.Throws<ArgumentException>(() => env.Rename(" "));
    }

    [Fact]
    public async Task CreateHandler_Persists_Environment()
    {
        var repo = new ProjectEnvironmentRepository(_db);
        var handler = new DeveloperPlatform.Infrastructure.Environments.CreateEnvironmentCommandHandler(
            repo, new TestExecutionContext { TenantId = _tenant });
        var result = await handler.HandleAsync(
            new DeveloperPlatform.Application.Environments.CreateEnvironment.CreateEnvironmentCommand(
                _project, "Staging", EnvironmentType.Staging));
        await _db.SaveChangesAsync();

        Assert.NotEqual(Guid.Empty, result.EnvironmentId);
        Assert.Single(await repo.ListAsync(_project));
    }

    [Fact]
    public async Task DeleteHandler_Cascades_To_Secrets()
    {
        var env = ProjectEnvironment.Create(_tenant, _project, "Dev", EnvironmentType.Development);
        _db.ProjectEnvironments.Add(env);
        _db.Secrets.Add(DeveloperPlatform.Domain.Secrets.Secret.Create(
            _tenant, _project, env.Id, "API_KEY", new byte[] { 1, 2, 3 }, Guid.NewGuid()));
        await _db.SaveChangesAsync();

        var handler = new DeveloperPlatform.Infrastructure.Environments.DeleteEnvironmentCommandHandler(
            new ProjectEnvironmentRepository(_db), _db);
        await handler.HandleAsync(
            new DeveloperPlatform.Application.Environments.DeleteEnvironment.DeleteEnvironmentCommand(_project, env.Id));
        await _db.SaveChangesAsync();

        Assert.Empty(await _db.ProjectEnvironments.ToListAsync());
        Assert.Empty(await _db.Secrets.ToListAsync());
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        public Guid TenantId { get; set; }
        public Guid? PrincipalId => null;
        public PrincipalType? PrincipalType => null;
        public Guid? UserId => null;
        public Guid? ProjectId => null;
        public Guid? EnvironmentId => null;
        public string IpAddress => "127.0.0.1";
        public bool IsCrossTenantOperation { get; set; }
    }
}
