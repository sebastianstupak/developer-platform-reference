using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Audit;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DeveloperPlatform.Api.Tests.Audit;

// Reproduces the outbox-relay bug: AuditOutboxEntry is ITenantScoped, so the global
// tenant query filter (IsCrossTenantOperation || TenantId == ctx.TenantId) applies to
// GetPendingAsync. The OutboxRelayWorker runs in a background scope with no HTTP request,
// so ctx.TenantId is Guid.Empty and the filter silently excludes every real entry.
public class OutboxRelayTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private MutableExecutionContext _ctx = null!;
    private readonly Guid _tenant = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _ctx = new MutableExecutionContext { TenantId = _tenant };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        _db = new ApplicationDbContext(options, _ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private AuditOutboxEntry SeedEntry() => AuditOutboxEntry.Create(
        _tenant, "SetSecretCommand", AuditStatus.Success, Guid.NewGuid(), "Member", Guid.NewGuid(),
        null, null, "127.0.0.1", false, null, new byte[] { 1, 2, 3 }, Guid.NewGuid());

    [Fact]
    public async Task GetPending_Returns_Entries_From_Background_Scope_Without_Tenant()
    {
        _db.AuditOutboxEntries.Add(SeedEntry());
        await _db.SaveChangesAsync();

        // Simulate the OutboxRelayWorker's background scope: no HTTP request ran, so the
        // execution context has no tenant.
        _ctx.TenantId = Guid.Empty;

        var repo = new AuditOutboxRepository(_db);
        var pending = await repo.GetPendingAsync(50);

        Assert.Single(pending);
    }

    [Fact]
    public async Task MarkProcessed_Works_From_Background_Scope_Without_Tenant()
    {
        var entry = SeedEntry();
        _db.AuditOutboxEntries.Add(entry);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear(); // force a cold lookup (not served from the identity map)

        _ctx.TenantId = Guid.Empty;

        var repo = new AuditOutboxRepository(_db);
        await repo.MarkProcessedAsync(entry.Id);

        var reloaded = await _db.AuditOutboxEntries.IgnoreQueryFilters()
            .SingleAsync(e => e.Id == entry.Id);
        Assert.NotNull(reloaded.ProcessedAt);
    }

    private sealed class MutableExecutionContext : IExecutionContext
    {
        public Guid TenantId { get; set; }
        public Guid? PrincipalId { get; set; }
        public PrincipalType? PrincipalType { get; set; }
        public Guid? UserId { get; set; }
        public Guid? ProjectId { get; set; }
        public Guid? EnvironmentId { get; set; }
        public string IpAddress { get; set; } = "127.0.0.1";
        public bool IsCrossTenantOperation { get; set; }
    }
}
