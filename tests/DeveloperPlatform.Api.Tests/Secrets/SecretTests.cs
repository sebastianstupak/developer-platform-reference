using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Secrets;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DeveloperPlatform.Api.Tests.Secrets;

public class SecretTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _project = Guid.NewGuid();
    private readonly Guid _env = Guid.NewGuid();
    private static readonly byte[] Key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

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
    public void UpdateValue_Sets_UpdatedAt_Later()
    {
        var s = Secret.Create(_tenant, _project, _env, "DB_URL", new byte[] { 1 }, Guid.NewGuid());
        var created = s.UpdatedAt;
        s.UpdateValue(new byte[] { 2 }, Guid.NewGuid());
        Assert.True(s.UpdatedAt >= created);
        Assert.Equal(2, s.EncryptedValue[0]);
    }

    [Fact]
    public async Task Repository_Get_By_Environment_And_Name()
    {
        var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
        await repo.AddAsync(Secret.Create(_tenant, _project, _env, "TOKEN", new byte[] { 9 }, Guid.NewGuid()));
        await _db.SaveChangesAsync();
        Assert.NotNull(await repo.GetAsync(_env, "TOKEN"));
        Assert.Null(await repo.GetAsync(_env, "MISSING"));
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
