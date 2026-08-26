using System.Security.Cryptography;
using System.Text;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.ApiKeys;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class ApiKeyAuthenticationHandlerTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _sa = Guid.NewGuid();
    private const string Plaintext = "dpk_TESTKEY_abcdefghijklmnop";

    public async Task InitializeAsync()
    {
        // Note: a DIFFERENT tenant is set on the context, to prove the lookup ignores the tenant filter.
        var ctx = new TestExecutionContext { TenantId = Guid.NewGuid() };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Plaintext)));
        _db.ApiKeyCredentials.Add(ApiKeyCredential.Create(_tenant, _sa, "k", "dpk_TESTKEY_", hash, null));
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Resolves_Live_Key_Across_Tenant_Filter()
    {
        var resolved = await ApiKeyAuthenticationHandler.ResolveCredentialAsync(_db, Plaintext, DateTime.UtcNow);
        Assert.NotNull(resolved);
        Assert.Equal(_sa, resolved!.Value.PrincipalId);
        Assert.Equal(_tenant, resolved.Value.TenantId);
    }

    [Fact]
    public async Task Rejects_Unknown_Key()
    {
        Assert.Null(await ApiKeyAuthenticationHandler.ResolveCredentialAsync(_db, "dpk_nope", DateTime.UtcNow));
    }

    [Fact]
    public async Task Rejects_Expired_And_Revoked()
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("dpk_expired")));
        _db.ApiKeyCredentials.Add(ApiKeyCredential.Create(_tenant, _sa, "e", "dpk_exp", hash, DateTime.UtcNow.AddDays(-1)));
        await _db.SaveChangesAsync();
        Assert.Null(await ApiKeyAuthenticationHandler.ResolveCredentialAsync(_db, "dpk_expired", DateTime.UtcNow));
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
