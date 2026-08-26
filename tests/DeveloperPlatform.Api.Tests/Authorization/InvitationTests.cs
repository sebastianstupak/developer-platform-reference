using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Members.InviteMember;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Authorization;
using DeveloperPlatform.Infrastructure.Members;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class InvitationTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();
    private Ctx _ctx = null!;

    public async Task InitializeAsync()
    {
        _ctx = new Ctx { TenantId = _tenant, PrincipalId = _actor };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new ApplicationDbContext(options, _ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
    }
    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private InviteMemberCommandHandler Handler() =>
        new(_db, _ctx, new PrivilegeGuard(new AuthorizationService(_db), _db));

    [Fact]
    public async Task Invite_To_Viewer_Requires_Actor_To_Hold_Viewer_Permissions()
    {
        // Viewer = ProjectsRead, SecretsRead, AuditRead (seeded). Actor holds none → denied.
        await Assert.ThrowsAsync<DeveloperPlatform.Application.Authorization.ForbiddenException>(
            () => Handler().HandleAsync(new InviteMemberCommand("new@example.com",
                DeveloperPlatform.Infrastructure.Authorization.SystemRoles.ViewerId, ScopeType.Tenant, null)));
    }

    [Fact]
    public async Task Invite_Creates_Pending_Invitation_When_Actor_Is_Owner()
    {
        // Owner role assignment for the actor → holds everything.
        _db.RoleAssignments.Add(RoleAssignment.Create(
            _tenant, _actor, DeveloperPlatform.Infrastructure.Authorization.SystemRoles.OwnerId, Scope.Tenant));
        await _db.SaveChangesAsync();

        var result = await Handler().HandleAsync(new InviteMemberCommand("new@example.com",
            DeveloperPlatform.Infrastructure.Authorization.SystemRoles.ViewerId, ScopeType.Tenant, null));

        var inv = await _db.Invitations.AsNoTracking().SingleAsync(i => i.Id == result.InvitationId);
        Assert.Equal("new@example.com", inv.Email);
        Assert.Equal(InvitationStatus.Pending, inv.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
    }

    private sealed class Ctx : IExecutionContext
    {
        public Guid TenantId { get; set; }
        public Guid? PrincipalId { get; set; }
        public PrincipalType? PrincipalType => DeveloperPlatform.Domain.Authorization.PrincipalType.Member;
        public Guid? UserId => null;
        public Guid? ProjectId => null;
        public Guid? EnvironmentId => null;
        public string IpAddress => "127.0.0.1";
        public bool IsCrossTenantOperation { get; set; }
    }
}
