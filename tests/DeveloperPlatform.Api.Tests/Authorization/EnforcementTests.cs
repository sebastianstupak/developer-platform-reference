using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Audit;
using DeveloperPlatform.Infrastructure.Context;
using DeveloperPlatform.Infrastructure.Crypto;
using DeveloperPlatform.Infrastructure.Dispatching;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class EnforcementTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private HttpExecutionContext _ctx = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _principal = Guid.NewGuid();
    private static readonly byte[] Key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    public async Task InitializeAsync()
    {
        _ctx = new HttpExecutionContext { TenantId = _tenant, IpAddress = "127.0.0.1", PrincipalId = _principal, PrincipalType = PrincipalType.Member };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        _db = new ApplicationDbContext(options, _ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private CommandDispatcher Build()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<GuardedCommand, Unit>, GuardedHandler>();
        var sp = services.BuildServiceProvider();
        var authz = new DeveloperPlatform.Infrastructure.Authorization.AuthorizationService(_db);
        return new CommandDispatcher(sp, _db, _ctx, new TenantCryptoService(_db, Key),
            new AuditOutboxRepository(_db), new SensitiveDataScrubber(), TenancyMode.SharedTables, authz);
    }

    [Fact]
    public async Task Guarded_Command_Throws_Forbidden_Without_Permission()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => Build().SendAsync<GuardedCommand, Unit>(new GuardedCommand()));
    }

    [Fact]
    public async Task Guarded_Command_Succeeds_With_Grant()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsWrite, Scope.Tenant));
        await _db.SaveChangesAsync();

        var result = await Build().SendAsync<GuardedCommand, Unit>(new GuardedCommand());
        Assert.Equal(Unit.Value, result);
    }

    [Fact]
    public async Task Guarded_Command_Throws_Forbidden_When_No_Principal()
    {
        _ctx.PrincipalId = null;
        await Assert.ThrowsAsync<ForbiddenException>(
            () => Build().SendAsync<GuardedCommand, Unit>(new GuardedCommand()));
    }

    [Fact]
    public async Task Guarded_Query_Throws_Forbidden_Without_Permission()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => BuildQuery().SendAsync<GuardedQuery, int>(new GuardedQuery()));
    }

    [Fact]
    public async Task Guarded_Query_Succeeds_With_Grant()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.ProjectsRead, Scope.Tenant));
        await _db.SaveChangesAsync();

        Assert.Equal(42, await BuildQuery().SendAsync<GuardedQuery, int>(new GuardedQuery()));
    }

    private QueryDispatcher BuildQuery()
    {
        var services = new ServiceCollection();
        services.AddScoped<DeveloperPlatform.Application.Queries.IQueryHandler<GuardedQuery, int>, GuardedQueryHandler>();
        var sp = services.BuildServiceProvider();
        return new QueryDispatcher(sp, _ctx, new DeveloperPlatform.Infrastructure.Authorization.AuthorizationService(_db));
    }

    [RequiresPermission(Permission.SecretsWrite)]
    public record GuardedCommand : ICommand;

    public class GuardedHandler : ICommandHandler<GuardedCommand, Unit>
    {
        public Task<Unit> HandleAsync(GuardedCommand command, CancellationToken ct = default)
            => Task.FromResult(Unit.Value);
    }

    [RequiresPermission(Permission.ProjectsRead)]
    public record GuardedQuery : DeveloperPlatform.Application.Queries.IQuery<int>;

    public class GuardedQueryHandler : DeveloperPlatform.Application.Queries.IQueryHandler<GuardedQuery, int>
    {
        public Task<int> HandleAsync(GuardedQuery query, CancellationToken ct = default)
            => Task.FromResult(42);
    }
}
