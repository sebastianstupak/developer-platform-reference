using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Projects;
using DeveloperPlatform.Domain.Secrets;
using DeveloperPlatform.Infrastructure.Persistence;
using DeveloperPlatform.Infrastructure.Projects;
using DeveloperPlatform.Application.Projects.GetProjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DeveloperPlatform.Api.Tests.Projects;

public class GetProjectsQueryTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenant = Guid.NewGuid();

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
    public async Task Counts_Environments_And_Reports_Last_Secret_Update()
    {
        var project = Project.Create(_tenant, "payments-api");
        _db.Projects.Add(project);
        var prod = ProjectEnvironment.Create(_tenant, project.Id, "Production", EnvironmentType.Production);
        var stg = ProjectEnvironment.Create(_tenant, project.Id, "Staging", EnvironmentType.Staging);
        _db.ProjectEnvironments.AddRange(prod, stg);
        var older = Secret.Create(_tenant, project.Id, prod.Id, "A", new byte[] { 1 }, Guid.NewGuid());
        var newer = Secret.Create(_tenant, project.Id, prod.Id, "B", new byte[] { 1 }, Guid.NewGuid());
        typeof(Secret).GetProperty("UpdatedAt")!.SetValue(older, new DateTime(2026, 1, 1));
        typeof(Secret).GetProperty("UpdatedAt")!.SetValue(newer, new DateTime(2026, 6, 1));
        _db.Secrets.AddRange(older, newer);
        await _db.SaveChangesAsync();

        var result = await new GetProjectsQueryHandler(_db).HandleAsync(new GetProjectsQuery());

        var summary = Assert.Single(result);
        Assert.Equal(2, summary.EnvironmentCount);
        Assert.Equal(new DateTime(2026, 6, 1), summary.LastActivityAt);
    }

    [Fact]
    public async Task LastActivity_Falls_Back_To_CreatedAt_When_No_Secrets()
    {
        var project = Project.Create(_tenant, "empty");
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var result = await new GetProjectsQueryHandler(_db).HandleAsync(new GetProjectsQuery());

        var summary = Assert.Single(result);
        Assert.Equal(0, summary.EnvironmentCount);
        Assert.Equal(project.CreatedAt, summary.LastActivityAt);
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
