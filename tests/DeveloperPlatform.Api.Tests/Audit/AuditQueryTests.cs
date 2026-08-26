using DeveloperPlatform.Application.Audit.GetAuditEvents;
using DeveloperPlatform.Application.Common;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;
using DeveloperPlatform.Infrastructure.Audit;
using DeveloperPlatform.Infrastructure.Context;
using DeveloperPlatform.Infrastructure.Dispatching;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Api.Tests.Audit;

public class AuditQueryTests : IAsyncLifetime
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

    private AuditEvent Ev(string cmd, AuditStatus status, DateTime when, Guid? principalId = null,
        string? principalType = null, Guid? userId = null, bool crossTenant = false)
        => AuditEvent.Create(_tenant, when, cmd, status, principalId, principalType, userId,
            null, null, "127.0.0.1", crossTenant, null, new byte[] { 1 }, Guid.NewGuid());

    [Fact]
    public async Task Lists_Newest_First_With_Total_And_Paging()
    {
        for (var i = 0; i < 30; i++)
        {
            _db.AuditEvents.Add(Ev($"Cmd{i}", AuditStatus.Success, new DateTime(2026, 1, 1).AddMinutes(i)));
        }

        await _db.SaveChangesAsync();

        var handler = new GetAuditEventsQueryHandler(_db);
        var page1 = await handler.HandleAsync(new GetAuditEventsQuery(
            new AuditFilter(null, null, null, null, null, null), 1, 25));

        Assert.Equal(30, page1.Total);
        Assert.Equal(25, page1.Items.Count);
        Assert.Equal("Cmd29", page1.Items[0].CommandType); // newest first
    }

    [Fact]
    public async Task Filters_By_Status_And_CommandType()
    {
        _db.AuditEvents.Add(Ev("SetSecretCommand", AuditStatus.Success, new DateTime(2026, 1, 1)));
        _db.AuditEvents.Add(Ev("SetSecretCommand", AuditStatus.Failed, new DateTime(2026, 1, 2)));
        _db.AuditEvents.Add(Ev("RevealSecretCommand", AuditStatus.Success, new DateTime(2026, 1, 3)));
        await _db.SaveChangesAsync();

        var handler = new GetAuditEventsQueryHandler(_db);
        var failed = await handler.HandleAsync(new GetAuditEventsQuery(
            new AuditFilter(null, null, null, "SetSecretCommand", AuditStatus.Failed, null), 1, 25));

        Assert.Single(failed.Items);
        Assert.Equal(AuditStatus.Failed, failed.Items[0].Status);
    }

    [Fact]
    public async Task Resolves_Member_Email_And_ServiceAccount_Name()
    {
        var user = User.Create("kc-sub", "dev@example.com", "Dev User");
        _db.Users.Add(user);
        var saPrincipal = Guid.NewGuid();
        _db.Principals.Add(Principal.CreateServiceAccount(_tenant, "ci"));
        _db.ServiceAccounts.Add(ServiceAccount.Create(_tenant, saPrincipal, "ci-deployer", null));
        _db.AuditEvents.Add(Ev("A", AuditStatus.Success, new DateTime(2026, 1, 2),
            principalId: Guid.NewGuid(), principalType: "Member", userId: user.Id));
        _db.AuditEvents.Add(Ev("B", AuditStatus.Success, new DateTime(2026, 1, 1),
            principalId: saPrincipal, principalType: "ServiceAccount"));
        await _db.SaveChangesAsync();

        var handler = new GetAuditEventsQueryHandler(_db);
        var res = await handler.HandleAsync(new GetAuditEventsQuery(
            new AuditFilter(null, null, null, null, null, null), 1, 25));

        Assert.Equal("dev@example.com", res.Items[0].ActorDisplay); // newest (A) = member
        Assert.Equal("ci-deployer", res.Items[1].ActorDisplay);     // (B) = service account
    }

    private QueryDispatcher BuildDispatcher(HttpExecutionContext ctx)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddScoped<IQueryHandler<GetAuditEventsQuery, PagedResult<AuditEventSummary>>,
            GetAuditEventsQueryHandler>();
        var sp = services.BuildServiceProvider();
        return new QueryDispatcher(sp, ctx,
            new DeveloperPlatform.Infrastructure.Authorization.AuthorizationService(_db));
    }

    [Fact]
    public async Task List_Forbidden_Without_AuditRead()
    {
        var ctx = new HttpExecutionContext { TenantId = _tenant, PrincipalId = Guid.NewGuid(), IpAddress = "127.0.0.1" };
        await Assert.ThrowsAsync<DeveloperPlatform.Application.Authorization.ForbiddenException>(() =>
            BuildDispatcher(ctx).SendAsync<GetAuditEventsQuery, PagedResult<AuditEventSummary>>(
                new GetAuditEventsQuery(new AuditFilter(null, null, null, null, null, null), 1, 25)));
    }

    [Fact]
    public async Task List_Allowed_With_AuditRead_Grant()
    {
        var principal = Guid.NewGuid();
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, principal, Permission.AuditRead, Scope.Tenant));
        await _db.SaveChangesAsync();
        var ctx = new HttpExecutionContext { TenantId = _tenant, PrincipalId = principal, IpAddress = "127.0.0.1" };
        var res = await BuildDispatcher(ctx).SendAsync<GetAuditEventsQuery, PagedResult<AuditEventSummary>>(
            new GetAuditEventsQuery(new AuditFilter(null, null, null, null, null, null), 1, 25));
        Assert.NotNull(res);
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
