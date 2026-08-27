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
        _sut = new PrincipalResolver(
            _db,
            new DeveloperPlatform.Infrastructure.Crypto.TenantCryptoService(
                _db, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
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
        Assert.True(await _db.TenantEncryptionKeys.AnyAsync());
    }

    [Fact]
    public async Task Existing_User_Self_Heals_Stale_Email_On_Login()
    {
        // First login creates the User (and the tenant Owner).
        await _sut.ResolveAsync(WithSubject("kc-heal"), _tenant);
        var user = await _db.Users.SingleAsync(u => u.KeycloakSubject == "kc-heal");
        user.UpdateProfile($"{user.KeycloakSubject}@unknown", "kc-heal"); // simulate a stale profile
        await _db.SaveChangesAsync();

        // A later login carrying a real email claim heals the stored profile.
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "kc-heal"),
            new Claim("email", "real@example.com"),
            new Claim("preferred_username", "Real Name"),
        }));
        await _sut.ResolveAsync(claims, _tenant);

        var healed = await _db.Users.AsNoTracking().SingleAsync(u => u.KeycloakSubject == "kc-heal");
        Assert.Equal("real@example.com", healed.Email);
        Assert.Equal("Real Name", healed.DisplayName);
    }

    [Fact]
    public async Task Second_Member_Without_Invitation_Gets_No_Membership()
    {
        await _sut.ResolveAsync(WithSubject("kc-first"), _tenant);       // first → Owner
        var second = await _sut.ResolveAsync(WithSubject("kc-second"), _tenant);
        Assert.Null(second);                                             // no invite → not a member
    }

    [Fact]
    public async Task Invited_User_Gets_Invited_Role_And_Invitation_Accepted()
    {
        await _sut.ResolveAsync(WithSubject("kc-first"), _tenant);       // establish tenant (Owner)
        var roleId = DeveloperPlatform.Infrastructure.Authorization.SystemRoles.ViewerId;
        _db.Invitations.Add(DeveloperPlatform.Domain.Authorization.Invitation.Create(
            _tenant, "invitee@example.com", roleId,
            DeveloperPlatform.Domain.Authorization.Scope.Tenant, "tok", DateTime.UtcNow.AddDays(1)));
        await _db.SaveChangesAsync();

        var claims = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("sub", "kc-invitee"),
            new System.Security.Claims.Claim("email", "invitee@example.com"),
            new System.Security.Claims.Claim("email_verified", "true"),
        }));
        var resolved = await _sut.ResolveAsync(claims, _tenant);

        Assert.NotNull(resolved);
        Assert.True(await _db.RoleAssignments.AnyAsync(a => a.PrincipalId == resolved!.PrincipalId && a.RoleId == roleId));
        Assert.True(await _db.Invitations.AnyAsync(i =>
            i.Email == "invitee@example.com" && i.Status == DeveloperPlatform.Domain.Authorization.InvitationStatus.Accepted));
    }

    [Fact]
    public async Task Invited_User_Without_Verified_Email_Is_Not_Onboarded()
    {
        await _sut.ResolveAsync(WithSubject("kc-first"), _tenant);   // establish tenant/Owner
        var roleId = DeveloperPlatform.Infrastructure.Authorization.SystemRoles.ViewerId;
        _db.Invitations.Add(DeveloperPlatform.Domain.Authorization.Invitation.Create(
            _tenant, "invitee@example.com", roleId,
            DeveloperPlatform.Domain.Authorization.Scope.Tenant, "tok", DateTime.UtcNow.AddDays(1)));
        await _db.SaveChangesAsync();

        var claims = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("sub", "kc-invitee2"),
            new System.Security.Claims.Claim("email", "invitee@example.com"),
            // no email_verified claim
        }));
        Assert.Null(await _sut.ResolveAsync(claims, _tenant));   // unverified email → not onboarded
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
