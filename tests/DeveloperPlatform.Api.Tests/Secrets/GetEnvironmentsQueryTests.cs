using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Environments.GetEnvironments;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Projects;
using DeveloperPlatform.Domain.Secrets;
using DeveloperPlatform.Infrastructure.Environments;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DeveloperPlatform.Api.Tests.Secrets;

public class GetEnvironmentsQueryTests : IAsyncLifetime
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
    public async Task Reports_Secret_Count_And_Last_Update_Per_Environment()
    {
        var prod = ProjectEnvironment.Create(_tenant, _project, "Production", EnvironmentType.Production);
        var dev = ProjectEnvironment.Create(_tenant, _project, "Development", EnvironmentType.Development);
        _db.ProjectEnvironments.AddRange(prod, dev);
        var s1 = Secret.Create(_tenant, _project, prod.Id, "A", new byte[] { 1 }, Guid.NewGuid());
        var s2 = Secret.Create(_tenant, _project, prod.Id, "B", new byte[] { 1 }, Guid.NewGuid());
        typeof(Secret).GetProperty("UpdatedAt")!.SetValue(s1, new DateTime(2026, 1, 1));
        typeof(Secret).GetProperty("UpdatedAt")!.SetValue(s2, new DateTime(2026, 3, 1));
        _db.Secrets.AddRange(s1, s2);
        await _db.SaveChangesAsync();

        var result = await new GetEnvironmentsQueryHandler(_db)
            .HandleAsync(new GetEnvironmentsQuery(_project));

        // Ordered by Name: Development, Production
        Assert.Equal("Development", result[0].Name);
        Assert.Equal(0, result[0].SecretCount);
        Assert.Equal(result[0].CreatedAt, result[0].LastUpdatedAt);
        Assert.Equal("Production", result[1].Name);
        Assert.Equal(2, result[1].SecretCount);
        Assert.Equal(new DateTime(2026, 3, 1), result[1].LastUpdatedAt);
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
