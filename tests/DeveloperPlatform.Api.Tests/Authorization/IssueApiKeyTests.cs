using DeveloperPlatform.Application.ApiKeys.IssueApiKey;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.ApiKeys;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class IssueApiKeyTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private Guid _saId;

    public async Task InitializeAsync()
    {
        var ctx = new TestExecutionContext { TenantId = _tenant };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
        var sa = Principal.CreateServiceAccount(_tenant, "ci-deployer");
        _db.Principals.Add(sa);
        _db.ServiceAccounts.Add(ServiceAccount.Create(_tenant, sa.Id, "ci-deployer", null));
        await _db.SaveChangesAsync();
        _saId = sa.Id;
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Issue_Returns_Plaintext_Once_And_Persists_Only_Hash()
    {
        var handler = new IssueApiKeyCommandHandler(_db, new TestExecutionContext { TenantId = _tenant });
        var result = await handler.HandleAsync(new IssueApiKeyCommand(_saId, "prod-key", null));

        Assert.StartsWith("dpk_", result.PlaintextKey);
        Assert.StartsWith("dpk_", result.KeyPrefix);
        Assert.True(result.KeyPrefix.Length <= result.PlaintextKey.Length);

        var cred = await _db.ApiKeyCredentials.AsNoTracking().SingleAsync();
        Assert.Equal(_saId, cred.ServiceAccountId);
        Assert.DoesNotContain(result.PlaintextKey, cred.KeyHash);   // hash, not plaintext
        Assert.Equal(64, cred.KeyHash.Length);                       // SHA-256 hex
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
