using System.Security.Claims;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class PrincipalResolverTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private PrincipalResolver _sut = null!;
    private readonly Guid _tenant = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var ctx = new TestExecutionContext { TenantId = _tenant };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
        _sut = new PrincipalResolver(_db);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static ClaimsPrincipal WithSubject(string? sub)
    {
        var claims = new List<Claim> { new("email", "u@example.com"), new("preferred_username", "u") };
        if (sub is not null)
        { claims.Add(new Claim("sub", sub)); }
        return new ClaimsPrincipal(new ClaimsIdentity(claims));
    }

    [Fact]
    public async Task No_Subject_Returns_Null()
    {
        Assert.Null(await _sut.ResolveAsync(WithSubject(null), _tenant));
    }

    [Fact]
    public async Task First_Member_Becomes_Owner()
    {
        var result = await _sut.ResolveAsync(WithSubject("kc-first"), _tenant);

        Assert.NotNull(result);
        Assert.Equal(PrincipalType.Member, result!.Type);
        var owner = await _db.RoleAssignments.AsNoTracking()
            .SingleOrDefaultAsync(a => a.PrincipalId == result.PrincipalId && a.RoleId == SystemRoles.OwnerId);
        Assert.NotNull(owner);
        Assert.Equal(ScopeType.Tenant, owner!.ScopeType);
    }

    [Fact]
    public async Task Second_Member_Gets_No_Role()
    {
        await _sut.ResolveAsync(WithSubject("kc-first"), _tenant);
        var second = await _sut.ResolveAsync(WithSubject("kc-second"), _tenant);

        Assert.NotNull(second);
        var assignments = await _db.RoleAssignments.AsNoTracking()
            .Where(a => a.PrincipalId == second!.PrincipalId).ToListAsync();
        Assert.Empty(assignments);
    }

    [Fact]
    public async Task Existing_Membership_Returns_Same_Principal_Without_Creating()
    {
        var first = await _sut.ResolveAsync(WithSubject("kc-first"), _tenant);
        var membershipCount = await _db.Memberships.CountAsync();

        var again = await _sut.ResolveAsync(WithSubject("kc-first"), _tenant);

        Assert.Equal(first!.PrincipalId, again!.PrincipalId);
        Assert.Equal(membershipCount, await _db.Memberships.CountAsync());
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        public Guid TenantId { get; set; }
        public Guid? PrincipalId => null;
        public DeveloperPlatform.Domain.Authorization.PrincipalType? PrincipalType => null;
        public Guid? UserId => null;
        public Guid? ProjectId => null;
        public Guid? EnvironmentId => null;
        public string IpAddress => "127.0.0.1";
        public bool IsCrossTenantOperation { get; set; }
    }
}
