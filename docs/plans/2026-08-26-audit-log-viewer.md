# Audit Log Viewer (Phase 6) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A paged, filterable Blazor view of the tenant's audit trail (`AuditEvents`), gated by `audit:read`, with an on-demand decrypted payload detail.

**Architecture:** Vertical-slice CQRS. Two read queries — a list over the denormalized `AuditEvents` columns (no decryption; actor GUIDs resolved to names via a post-query dictionary lookup) and a single-event detail that decrypts one payload with a graceful fallback — plus a distinct-command-types query, exposed under `/api/v1/audit`, consumed by an `/audit` Blazor page with server-side paging.

**Tech Stack:** .NET 10, EF Core 10 + Pomelo MySQL, minimal APIs (Asp.Versioning), Blazor Server + MudBlazor v9, xUnit + EF in-memory.

## Global Constraints

- All three queries carry `[RequiresPermission(Permission.AuditRead)]` and implement `IResourceScoped` returning `Scope.Tenant`, pinning the permission check at tenant scope regardless of any ambient `project_id`/`environment_id` claim (do NOT rely on the dispatcher's default — a scoped claim would otherwise narrow the check while the data stays tenant-wide).
- The list query must NOT decrypt payloads; the detail query decrypts exactly one.
- Detail decryption degrades gracefully: catch the crypto failure (missing/shredded key) and return `PayloadAvailable = false`, never a 500.
- `AuditEvent` is `ITenantScoped` → the global query filter bounds every read to the caller's tenant automatically.
- Reads go through `QueryDispatcher`; handlers query `ApplicationDbContext` directly (repositories are for writes). Queries are NOT audited.
- Repo conventions: no AI co-author trailers; hooks must pass (never `--no-verify`; if a commit times out on a cold build, `dotnet build --no-restore` then retry). `.cs` files are CRLF; files written by the Write tool land as LF — normalize new `.cs` to CRLF before commit if the format hook complains. New `.razor`/`.json` normalize automatically.
- Global interactive render mode is set on `<Routes>` in `App.razor` — do NOT add `@rendermode` to any component.
- Stop the running API before a solution-scope `dotnet test` (it locks Infrastructure/Domain/Application DLLs); per-project `dotnet test tests/DeveloperPlatform.Api.Tests` is fine.

## Conventions reference (verified against the codebase)

- **Query:** `[RequiresPermission(Permission.X)] public record FooQuery(...) : IQuery<TResult>, IResourceScoped { public Scope ResourceScope => Scope.Tenant; }` — the audit queries pin `Scope.Tenant` explicitly rather than relying on the dispatcher default.
- **Query handler:** `public sealed class FooQueryHandler(ApplicationDbContext db) : IQueryHandler<FooQuery, TResult> { public async Task<TResult> HandleAsync(FooQuery query, CancellationToken ct = default) {...} }`. Extra deps (`ITenantCryptoService`, `IExecutionContext`) are constructor-injected.
- **DI:** register each handler in `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs`.
- **Endpoints:** `public static class FooEndpoints { public static IEndpointRouteBuilder MapFoo(this IEndpointRouteBuilder app, ApiVersionSet versionSet) {...} }`, registered in `src/DeveloperPlatform.Api/Program.cs`.
- **Facts:** `AuditEvent` (namespace `DeveloperPlatform.Domain.Audit`) fields — `Id, OccurredAt, CommandType, AuditStatus Status, Guid? PrincipalId, string? PrincipalType, Guid? UserId, Guid? ProjectId, Guid? EnvironmentId, string IpAddress, bool IsCrossTenant, string? CrossTenantReason, byte[] EncryptedPayload, Guid KeyId`. `AuditStatus { Success, Failed }`. `PrincipalType` string is `"Member"` or `"ServiceAccount"`. DbSet is `db.AuditEvents`.
- `User` (`DeveloperPlatform.Domain.Identity`, NOT tenant-scoped): `Id, Email, DisplayName`. `db.Users`. `ServiceAccount` (`DeveloperPlatform.Domain.Authorization`): `PrincipalId, Name`. `db.ServiceAccounts`.
- `ITenantCryptoService.DecryptAsync(Guid tenantId, byte[] payload, Guid keyId, CancellationToken)` → `string`; throws `InvalidOperationException` when the key is missing/shredded.
- **Handler unit-test harness** (copy from `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`): in-memory `ApplicationDbContext` with `.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`, a private nested `TestExecutionContext : IExecutionContext { TenantId set }`, and `Key = RandomNumberGenerator.GetBytes(32)` for crypto. Construct `TenantCryptoService(_db, Key)` and `await crypto.CreateKeyAsync(_tenant); await _db.SaveChangesAsync();` to seed a key.
- **Query-dispatcher authz harness** (copy `BuildQuery()` from `tests/DeveloperPlatform.Api.Tests/Authorization/EnforcementTests.cs`): `new QueryDispatcher(sp, _ctx, new AuthorizationService(_db))` with the handler registered in a local `ServiceCollection`; seed permission via `PermissionGrant.Create(_tenant, _principal, Permission.AuditRead, Scope.Tenant)`.

## File Structure

**Slice A — Backend**
- Create `src/DeveloperPlatform.Application/Common/PagedResult.cs`.
- Create `src/DeveloperPlatform.Application/Audit/GetAuditEvents/GetAuditEventsQuery.cs` (query + `AuditFilter` + `AuditEventSummary`).
- Create `src/DeveloperPlatform.Application/Audit/GetAuditEventDetail/GetAuditEventDetailQuery.cs` (query + `AuditEventDetail`).
- Create `src/DeveloperPlatform.Application/Audit/GetAuditCommandTypes/GetAuditCommandTypesQuery.cs`.
- Create `src/DeveloperPlatform.Infrastructure/Audit/GetAuditEventsQueryHandler.cs`, `GetAuditEventDetailQueryHandler.cs`, `GetAuditCommandTypesQueryHandler.cs`.
- Modify `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs`.
- Create `src/DeveloperPlatform.Api/Endpoints/Audit/AuditEndpoints.cs`; modify `src/DeveloperPlatform.Api/Program.cs`.
- Test `tests/DeveloperPlatform.Api.Tests/Audit/AuditQueryTests.cs`.

**Slice B — Web UI**
- Modify `src/DeveloperPlatform.Web/Http/DeveloperPlatformApiClient.cs`; create `src/DeveloperPlatform.Web/Http/Models/AuditDtos.cs`.
- Create `src/DeveloperPlatform.Web/Components/Pages/Audit.razor` + `AuditDetailDrawer.razor` (or a dialog).
- Modify `src/DeveloperPlatform.Web/Components/Layout/NavMenu.razor`.
- Test `tests/DeveloperPlatform.Web.Tests/Http/DeveloperPlatformApiClientTests.cs`.

---

## Slice A — Backend query API

### Task A1: PagedResult + list query (filters, paging, actor resolution)

**Files:**
- Create: `src/DeveloperPlatform.Application/Common/PagedResult.cs`, `src/DeveloperPlatform.Application/Audit/GetAuditEvents/GetAuditEventsQuery.cs`, `src/DeveloperPlatform.Infrastructure/Audit/GetAuditEventsQueryHandler.cs`
- Create: `src/DeveloperPlatform.Api/Endpoints/Audit/AuditEndpoints.cs`
- Modify: `ServiceCollectionExtensions.cs`, `Program.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Audit/AuditQueryTests.cs`

**Interfaces:**
- Produces: `PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)`; `GetAuditEventsQuery(AuditFilter Filter, int Page, int PageSize) : IQuery<PagedResult<AuditEventSummary>>`; `AuditFilter(DateTime? From, DateTime? To, Guid? PrincipalId, string? CommandType, AuditStatus? Status, bool? CrossTenantOnly)`; `AuditEventSummary(Guid Id, DateTime OccurredAt, string CommandType, AuditStatus Status, string? ActorDisplay, string? PrincipalType, string IpAddress, bool IsCrossTenant, Guid? ProjectId, Guid? EnvironmentId)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DeveloperPlatform.Api.Tests/Audit/AuditQueryTests.cs`:
```csharp
using DeveloperPlatform.Application.Audit.GetAuditEvents;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;
using DeveloperPlatform.Infrastructure.Audit;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
            _db.AuditEvents.Add(Ev($"Cmd{i}", AuditStatus.Success, new DateTime(2026, 1, 1).AddMinutes(i)));
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
}
```
Add the private nested `TestExecutionContext` (copy from `SecretTests.cs`).

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/DeveloperPlatform.Api.Tests --filter AuditQueryTests` → FAIL (types missing).

- [ ] **Step 3: Create `PagedResult<T>`**

`src/DeveloperPlatform.Application/Common/PagedResult.cs`:
```csharp
namespace DeveloperPlatform.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
```

- [ ] **Step 4: Create the query**

`src/DeveloperPlatform.Application/Audit/GetAuditEvents/GetAuditEventsQuery.cs`:
```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Common;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Audit.GetAuditEvents;

[RequiresPermission(Permission.AuditRead)]
public record GetAuditEventsQuery(AuditFilter Filter, int Page, int PageSize)
    : IQuery<PagedResult<AuditEventSummary>>;

public record AuditFilter(
    DateTime? From, DateTime? To, Guid? PrincipalId, string? CommandType,
    AuditStatus? Status, bool? CrossTenantOnly);

public record AuditEventSummary(
    Guid Id, DateTime OccurredAt, string CommandType, AuditStatus Status,
    string? ActorDisplay, string? PrincipalType, string IpAddress, bool IsCrossTenant,
    Guid? ProjectId, Guid? EnvironmentId);
```

- [ ] **Step 5: Create the handler**

`src/DeveloperPlatform.Infrastructure/Audit/GetAuditEventsQueryHandler.cs`:
```csharp
using DeveloperPlatform.Application.Audit.GetAuditEvents;
using DeveloperPlatform.Application.Common;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Audit;

public sealed class GetAuditEventsQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetAuditEventsQuery, PagedResult<AuditEventSummary>>
{
    public async Task<PagedResult<AuditEventSummary>> HandleAsync(
        GetAuditEventsQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize <= 0 ? 25 : query.PageSize, 1, 100);
        var f = query.Filter;

        var q = db.AuditEvents.AsNoTracking();
        if (f.From is { } from) q = q.Where(e => e.OccurredAt >= from);
        if (f.To is { } to) q = q.Where(e => e.OccurredAt <= to);
        if (f.PrincipalId is { } pid) q = q.Where(e => e.PrincipalId == pid);
        if (!string.IsNullOrWhiteSpace(f.CommandType)) q = q.Where(e => e.CommandType == f.CommandType);
        if (f.Status is { } st) q = q.Where(e => e.Status == st);
        if (f.CrossTenantOnly == true) q = q.Where(e => e.IsCrossTenant);

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);

        var userIds = rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var principalIds = rows.Where(r => r.PrincipalId is not null).Select(r => r.PrincipalId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        var sas = await db.ServiceAccounts.AsNoTracking()
            .Where(s => principalIds.Contains(s.PrincipalId)).ToDictionaryAsync(s => s.PrincipalId, s => s.Name, ct);

        var items = rows.Select(r => new AuditEventSummary(
            r.Id, r.OccurredAt, r.CommandType, r.Status,
            ResolveActor(r, users, sas), r.PrincipalType, r.IpAddress, r.IsCrossTenant,
            r.ProjectId, r.EnvironmentId)).ToList();

        return new PagedResult<AuditEventSummary>(items, total, page, size);
    }

    internal static string? ResolveActor(
        AuditEvent e, IReadOnlyDictionary<Guid, string> users, IReadOnlyDictionary<Guid, string> sas)
    {
        if (e.PrincipalType == "Member" && e.UserId is { } uid && users.TryGetValue(uid, out var email))
            return email;
        if (e.PrincipalType == "ServiceAccount" && e.PrincipalId is { } pid && sas.TryGetValue(pid, out var name))
            return name;
        return e.PrincipalId?.ToString();
    }
}
```

- [ ] **Step 6: DI** — in `ServiceCollectionExtensions.cs`, add an `// Audit (Slice A)` block:
```csharp
services.AddScoped<IQueryHandler<GetAuditEventsQuery, PagedResult<AuditEventSummary>>, GetAuditEventsQueryHandler>();
```
Add `using DeveloperPlatform.Application.Audit.GetAuditEvents;`, `using DeveloperPlatform.Application.Common;`, `using DeveloperPlatform.Infrastructure.Audit;`.

- [ ] **Step 7: Create the endpoint**

`src/DeveloperPlatform.Api/Endpoints/Audit/AuditEndpoints.cs`:
```csharp
using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Audit.GetAuditEvents;
using DeveloperPlatform.Application.Common;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Audit;

namespace DeveloperPlatform.Api.Endpoints.Audit;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAudit(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v1/audit")
            .WithTags("Audit").WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        group.MapGet("/", async (
            DateTime? from, DateTime? to, Guid? principalId, string? commandType,
            AuditStatus? status, bool? crossTenantOnly, int? page, int? pageSize,
            IQueryDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync<GetAuditEventsQuery, PagedResult<AuditEventSummary>>(
                new GetAuditEventsQuery(
                    new AuditFilter(from, to, principalId, commandType, status, crossTenantOnly),
                    page ?? 1, pageSize ?? 25), ct);

            return Results.Ok(new AuditPageResponse(
                result.Items.Select(i => new AuditEventResponse(
                    i.Id, i.OccurredAt, i.CommandType, i.Status.ToString(), i.ActorDisplay,
                    i.PrincipalType, i.IpAddress, i.IsCrossTenant, i.ProjectId, i.EnvironmentId)).ToList(),
                result.Total, result.Page, result.PageSize));
        }).WithName("GetAuditEvents").Produces<AuditPageResponse>();

        return app;
    }

    public record AuditEventResponse(
        Guid Id, DateTime OccurredAt, string CommandType, string Status, string? ActorDisplay,
        string? PrincipalType, string IpAddress, bool IsCrossTenant, Guid? ProjectId, Guid? EnvironmentId);

    public record AuditPageResponse(
        IReadOnlyList<AuditEventResponse> Items, int Total, int Page, int PageSize);
}
```
Register in `Program.cs` after `app.MapMembers(versionSet);`: `app.MapAudit(versionSet);` + `using DeveloperPlatform.Api.Endpoints.Audit;`.

- [ ] **Step 8: Add a dispatcher-level authz test** (append to `AuditQueryTests.cs`) — proves `audit:read` is enforced. The `QueryDispatcher` reads its OWN `IExecutionContext` for the authz check (independent of the one `_db` uses for the tenant filter), so you can pass a separate `HttpExecutionContext` carrying a `PrincipalId` without rebuilding `_db`. Add these usings to the test file: `using DeveloperPlatform.Application.Common;`, `using DeveloperPlatform.Application.Queries;`, `using DeveloperPlatform.Infrastructure.Context;`, `using DeveloperPlatform.Infrastructure.Dispatching;`, `using Microsoft.Extensions.DependencyInjection;`. Then:
```csharp
    private QueryDispatcher BuildDispatcher(HttpExecutionContext ctx)
    {
        var services = new ServiceCollection();
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
```
This needs `using DeveloperPlatform.Domain.Authorization;` (already present for `AuditStatus`? no — add it for `PermissionGrant`, `Permission`, `Scope`).

- [ ] **Step 9: Run tests / build** — `dotnet test tests/DeveloperPlatform.Api.Tests --filter AuditQueryTests` PASS; `dotnet build developer-platform-reference.slnx` 0 errors.

- [ ] **Step 10: Commit**
```bash
git add src/DeveloperPlatform.Application/Common src/DeveloperPlatform.Application/Audit/GetAuditEvents \
        src/DeveloperPlatform.Infrastructure/Audit/GetAuditEventsQueryHandler.cs \
        src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs \
        src/DeveloperPlatform.Api/Endpoints/Audit src/DeveloperPlatform.Api/Program.cs \
        tests/DeveloperPlatform.Api.Tests/Audit/AuditQueryTests.cs
git commit -m "feat(audit): paged/filtered audit events query + endpoint"
```

### Task A2: Detail query (decrypt one payload, graceful fallback)

**Files:**
- Create: `src/DeveloperPlatform.Application/Audit/GetAuditEventDetail/GetAuditEventDetailQuery.cs`, `src/DeveloperPlatform.Infrastructure/Audit/GetAuditEventDetailQueryHandler.cs`
- Modify: `ServiceCollectionExtensions.cs`, `AuditEndpoints.cs`
- Test: `AuditQueryTests.cs` (append)

**Interfaces:**
- Produces: `GetAuditEventDetailQuery(Guid Id) : IQuery<AuditEventDetail>`; `AuditEventDetail(AuditEventSummary Summary, string? CrossTenantReason, string PayloadJson, bool PayloadAvailable)`.

- [ ] **Step 1: Failing tests** (append to `AuditQueryTests.cs`)
```csharp
    [Fact]
    public async Task Detail_Decrypts_Payload()
    {
        var crypto = new DeveloperPlatform.Infrastructure.Crypto.TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant); await _db.SaveChangesAsync();
        var (payload, keyId) = await crypto.EncryptAsync(_tenant, "{\"Name\":\"DATABASE_URL\"}");
        var ev = AuditEvent.Create(_tenant, new DateTime(2026, 1, 1), "SetSecretCommand",
            AuditStatus.Success, null, null, null, null, null, "127.0.0.1", false, null, payload, keyId);
        _db.AuditEvents.Add(ev);
        await _db.SaveChangesAsync();

        var handler = new DeveloperPlatform.Infrastructure.Audit.GetAuditEventDetailQueryHandler(
            _db, crypto, new TestExecutionContext { TenantId = _tenant });
        var detail = await handler.HandleAsync(
            new DeveloperPlatform.Application.Audit.GetAuditEventDetail.GetAuditEventDetailQuery(ev.Id));

        Assert.True(detail.PayloadAvailable);
        Assert.Contains("DATABASE_URL", detail.PayloadJson);
    }

    [Fact]
    public async Task Detail_Marks_Unavailable_When_Key_Shredded()
    {
        var crypto = new DeveloperPlatform.Infrastructure.Crypto.TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant); await _db.SaveChangesAsync();
        var (payload, keyId) = await crypto.EncryptAsync(_tenant, "{\"x\":1}");
        var ev = AuditEvent.Create(_tenant, new DateTime(2026, 1, 1), "C", AuditStatus.Success,
            null, null, null, null, null, "127.0.0.1", false, null, payload, keyId);
        _db.AuditEvents.Add(ev);
        await _db.SaveChangesAsync();
        await crypto.ShredKeyAsync(_tenant); await _db.SaveChangesAsync();

        var handler = new DeveloperPlatform.Infrastructure.Audit.GetAuditEventDetailQueryHandler(
            _db, crypto, new TestExecutionContext { TenantId = _tenant });
        var detail = await handler.HandleAsync(
            new DeveloperPlatform.Application.Audit.GetAuditEventDetail.GetAuditEventDetailQuery(ev.Id));

        Assert.False(detail.PayloadAvailable);
        Assert.Equal("", detail.PayloadJson);
    }
```
This test class needs `private static readonly byte[] Key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);` — add it if not already present.

- [ ] **Step 2: Run — FAIL.**

- [ ] **Step 3: Query** `GetAuditEventDetail/GetAuditEventDetailQuery.cs`:
```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Audit.GetAuditEvents;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Audit.GetAuditEventDetail;

[RequiresPermission(Permission.AuditRead)]
public record GetAuditEventDetailQuery(Guid Id) : IQuery<AuditEventDetail>;

public record AuditEventDetail(
    AuditEventSummary Summary, string? CrossTenantReason, string PayloadJson, bool PayloadAvailable);
```

- [ ] **Step 4: Handler** `Audit/GetAuditEventDetailQueryHandler.cs`:
```csharp
using DeveloperPlatform.Application.Audit.GetAuditEventDetail;
using DeveloperPlatform.Application.Audit.GetAuditEvents;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Audit;

public sealed class GetAuditEventDetailQueryHandler(
    ApplicationDbContext db, ITenantCryptoService crypto, IExecutionContext ctx)
    : IQueryHandler<GetAuditEventDetailQuery, AuditEventDetail>
{
    public async Task<AuditEventDetail> HandleAsync(GetAuditEventDetailQuery query, CancellationToken ct = default)
    {
        var e = await db.AuditEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, ct)
            ?? throw new KeyNotFoundException($"Audit event {query.Id} not found.");

        var users = e.UserId is { } uid
            ? await db.Users.AsNoTracking().Where(u => u.Id == uid).ToDictionaryAsync(u => u.Id, u => u.Email, ct)
            : new Dictionary<Guid, string>();
        var sas = e.PrincipalId is { } pid
            ? await db.ServiceAccounts.AsNoTracking().Where(s => s.PrincipalId == pid).ToDictionaryAsync(s => s.PrincipalId, s => s.Name, ct)
            : new Dictionary<Guid, string>();

        var summary = new AuditEventSummary(
            e.Id, e.OccurredAt, e.CommandType, e.Status,
            GetAuditEventsQueryHandler.ResolveActor(e, users, sas), e.PrincipalType, e.IpAddress,
            e.IsCrossTenant, e.ProjectId, e.EnvironmentId);

        string payloadJson = "";
        var available = false;
        try
        {
            payloadJson = await crypto.DecryptAsync(ctx.TenantId, e.EncryptedPayload, e.KeyId, ct);
            available = true;
        }
        catch (InvalidOperationException)
        {
            // Key missing/shredded (e.g. rotated away or tenant crypto-shredded) — payload unrecoverable.
        }

        return new AuditEventDetail(summary, e.CrossTenantReason, payloadJson, available);
    }
}
```

- [ ] **Step 5: DI** `services.AddScoped<IQueryHandler<GetAuditEventDetailQuery, AuditEventDetail>, GetAuditEventDetailQueryHandler>();` + `using DeveloperPlatform.Application.Audit.GetAuditEventDetail;`.

- [ ] **Step 6: Endpoint** — add to `AuditEndpoints.cs`:
```csharp
        group.MapGet("/{id:guid}", async (Guid id, IQueryDispatcher d, CancellationToken ct) =>
        {
            var detail = await d.SendAsync<GetAuditEventDetailQuery, AuditEventDetail>(
                new GetAuditEventDetailQuery(id), ct);
            var s = detail.Summary;
            return Results.Ok(new AuditDetailResponse(
                new AuditEventResponse(s.Id, s.OccurredAt, s.CommandType, s.Status.ToString(), s.ActorDisplay,
                    s.PrincipalType, s.IpAddress, s.IsCrossTenant, s.ProjectId, s.EnvironmentId),
                detail.CrossTenantReason, detail.PayloadJson, detail.PayloadAvailable));
        }).WithName("GetAuditEventDetail").Produces<AuditDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
```
Add `public record AuditDetailResponse(AuditEventResponse Event, string? CrossTenantReason, string PayloadJson, bool PayloadAvailable);` and `using DeveloperPlatform.Application.Audit.GetAuditEventDetail;`. (`KeyNotFoundException` → 404 is already mapped globally by `RequestExceptionHandler`.)

- [ ] **Step 7: Run / build — PASS / 0 errors.**

- [ ] **Step 8: Commit** `git commit -m "feat(audit): audit event detail with on-demand payload decryption"`

### Task A3: Distinct command types query

**Files:** `src/DeveloperPlatform.Application/Audit/GetAuditCommandTypes/GetAuditCommandTypesQuery.cs`, `src/DeveloperPlatform.Infrastructure/Audit/GetAuditCommandTypesQueryHandler.cs`, DI, `AuditEndpoints.cs`, test.

- [ ] **Step 1: Failing test** (append to `AuditQueryTests.cs`)
```csharp
    [Fact]
    public async Task CommandTypes_Returns_Distinct_Sorted()
    {
        _db.AuditEvents.Add(Ev("BravoCommand", AuditStatus.Success, new DateTime(2026, 1, 1)));
        _db.AuditEvents.Add(Ev("AlphaCommand", AuditStatus.Success, new DateTime(2026, 1, 2)));
        _db.AuditEvents.Add(Ev("AlphaCommand", AuditStatus.Failed, new DateTime(2026, 1, 3)));
        await _db.SaveChangesAsync();

        var handler = new DeveloperPlatform.Infrastructure.Audit.GetAuditCommandTypesQueryHandler(_db);
        var types = await handler.HandleAsync(
            new DeveloperPlatform.Application.Audit.GetAuditCommandTypes.GetAuditCommandTypesQuery());

        Assert.Equal(new[] { "AlphaCommand", "BravoCommand" }, types);
    }
```

- [ ] **Step 2: Run — FAIL.**

- [ ] **Step 3: Query** `GetAuditCommandTypes/GetAuditCommandTypesQuery.cs`:
```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Audit.GetAuditCommandTypes;

[RequiresPermission(Permission.AuditRead)]
public record GetAuditCommandTypesQuery : IQuery<IReadOnlyList<string>>;
```

- [ ] **Step 4: Handler** `Audit/GetAuditCommandTypesQueryHandler.cs`:
```csharp
using DeveloperPlatform.Application.Audit.GetAuditCommandTypes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Audit;

public sealed class GetAuditCommandTypesQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetAuditCommandTypesQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> HandleAsync(GetAuditCommandTypesQuery query, CancellationToken ct = default)
        => await db.AuditEvents.AsNoTracking()
            .Select(e => e.CommandType).Distinct().OrderBy(c => c).ToListAsync(ct);
}
```

- [ ] **Step 5: DI** `services.AddScoped<IQueryHandler<GetAuditCommandTypesQuery, IReadOnlyList<string>>, GetAuditCommandTypesQueryHandler>();` + using.

- [ ] **Step 6: Endpoint** — add to `AuditEndpoints.cs`:
```csharp
        group.MapGet("/command-types", async (IQueryDispatcher d, CancellationToken ct) =>
        {
            var types = await d.SendAsync<GetAuditCommandTypesQuery, IReadOnlyList<string>>(
                new GetAuditCommandTypesQuery(), ct);
            return Results.Ok(types);
        }).WithName("GetAuditCommandTypes").Produces<IReadOnlyList<string>>();
```
Add `using DeveloperPlatform.Application.Audit.GetAuditCommandTypes;`. Route order is irrelevant: the `:guid` constraint on `/{id:guid}` (Task A2) means the literal `command-types` segment can never be captured as an id.

- [ ] **Step 7: Run / build — PASS.**

- [ ] **Step 8: Commit** `git commit -m "feat(audit): distinct command types query for the action filter"`

---

## Slice B — Web UI

> Blazor components are verified by build + manual Playwright walkthrough (the repo unit-tests only the API client). No `@rendermode` on components (global mode is on `<Routes>`). Follow the MudBlazor zinc theme and mirror `Members.razor`/`ServiceAccounts.razor` scaffolding (per-page `MudPopoverProvider`/`MudDialogProvider`/`MudSnackbarProvider`, `PersistentComponentState`→`TokenProvider` restore, `_loading` guard).

### Task B1: API client methods + DTOs (unit-tested)

**Files:** Modify `src/DeveloperPlatform.Web/Http/DeveloperPlatformApiClient.cs`; create `src/DeveloperPlatform.Web/Http/Models/AuditDtos.cs`; test `tests/DeveloperPlatform.Web.Tests/Http/DeveloperPlatformApiClientTests.cs`.

**Interfaces (produce):**
```csharp
Task<AuditPageDto> GetAuditEventsAsync(AuditFilterDto filter, int page, int pageSize);
Task<AuditDetailDto?> GetAuditEventDetailAsync(Guid id);
Task<IReadOnlyList<string>> GetAuditCommandTypesAsync();
```
DTOs (`AuditDtos.cs`):
```csharp
public record AuditEventDto(Guid Id, DateTime OccurredAt, string CommandType, string Status,
    string? ActorDisplay, string? PrincipalType, string IpAddress, bool IsCrossTenant,
    Guid? ProjectId, Guid? EnvironmentId);
public record AuditPageDto(IReadOnlyList<AuditEventDto> Items, int Total, int Page, int PageSize);
public record AuditDetailDto(AuditEventDto Event, string? CrossTenantReason, string PayloadJson, bool PayloadAvailable);
public record AuditFilterDto(DateTime? From, DateTime? To, Guid? PrincipalId, string? CommandType,
    string? Status, bool? CrossTenantOnly);
```

- [ ] **Step 1:** Write a client unit test (mirror the existing `DeveloperPlatformApiClientTests` stubbed-handler pattern) asserting `GetAuditEventsAsync` deserializes `Items` + `Total`. **Step 2:** run → FAIL. **Step 3:** implement the three methods — `GetAuditEventsAsync` builds a query string from the non-null filter fields + `page`/`pageSize` and GETs `/api/v1/audit`, deserializing `AuditPageDto` (swallow HTTP failure to `new AuditPageDto([], 0, page, pageSize)` per the list-method convention); `GetAuditEventDetailAsync` GETs `/api/v1/audit/{id}` (returns null on failure); `GetAuditCommandTypesAsync` GETs `/api/v1/audit/command-types` (returns `[]` on failure). Escape query values with `Uri.EscapeDataString`. **Step 4:** run → PASS. **Step 5:** commit `feat(web): API client for the audit log`.

### Task B2: Audit page — filter bar + server-paged grid + nav item

**Files:** Create `src/DeveloperPlatform.Web/Components/Pages/Audit.razor`; modify `NavMenu.razor`.

- [ ] Build `Audit.razor` (`@page "/audit"`, `@attribute [Authorize]`, inject `DeveloperPlatformApiClient`, `IDialogService`, `ISnackbar`):
  - A filter bar: two `MudDatePicker`s (From/To), an actor `MudSelect<Guid?>` whose items come from `GetMembersAsync()` (label = email) + `GetServiceAccountsAsync()` (label = name), value = principal id; an action `MudSelect<string?>` populated from `GetAuditCommandTypesAsync()`; a status `MudSelect<string?>` (Success/Failed/any); a cross-tenant `MudSwitch`. An "Apply"/auto-reload resets to page 1.
  - A `MudDataGrid<AuditEventDto>` with `ServerData` bound to a method that calls `GetAuditEventsAsync(currentFilter, page, pageSize)` and returns `new GridData<AuditEventDto> { Items = res.Items, TotalItems = res.Total }`. Columns: Time (`OccurredAt`), Actor (`ActorDisplay` — fall back to "system" when null), Action (`CommandType`), Status (a `MudChip` — `Color.Success` for "Success", `Color.Error` for "Failed"), Scope (`EnvironmentId ?? ProjectId` shown short, else "—"), IP. Show a small warning icon on `IsCrossTenant` rows. `RowsPerPageOptions` `{25, 50, 100}`.
  - Empty state via `NoRecordsContent`. If a load throws a 403-derived failure the grid simply shows empty; add a `MudAlert` note "You need audit:read to view this" is optional — the API returns 403 and the client swallows to empty.
- [ ] Add an "Audit" item to the **Access** `MudNavGroup` in `NavMenu.razor` (after Roles; use `Icons.Material.Outlined.History` or similar), `Href="audit"`.
- [ ] Verify `dotnet build developer-platform-reference.slnx` 0 errors; commit `feat(web): audit log page with filters and server-side paging`.

### Task B3: Detail drawer

**Files:** Create `src/DeveloperPlatform.Web/Components/Pages/AuditDetailDialog.razor`; wire row-click in `Audit.razor`.

- [ ] On grid row-click, call `GetAuditEventDetailAsync(id)` and open `AuditDetailDialog` showing: the metadata (time, actor, action, status, IP, scope, cross-tenant reason if any) and the payload. If `PayloadAvailable`, pretty-print `PayloadJson` in a monospace `MudPaper` (`JsonNode.Parse(...)` re-serialized with `WriteIndented = true`, guarded by try/catch so a non-JSON payload still shows raw). If not available, show `MudAlert` "Payload unavailable (encryption key rotated away or shredded)."
- [ ] Verify build; commit `feat(web): audit event detail drawer with decrypted payload`.

---

## Final verification (before finishing the branch)

- [ ] Stop the running API, then `dotnet test developer-platform-reference.slnx` — all suites green.
- [ ] Run the stack; manual Playwright pass: the `/audit` page lists events, filters by status/action/actor/time/cross-tenant, pages, and a row opens the detail drawer showing the decrypted scrubbed payload (perform a couple of secret operations first to generate events).
- [ ] Use superpowers:finishing-a-development-branch.

## Out of scope (YAGNI)

CSV/JSON export; live tail/streaming; retention/archival; full-text payload search; project/environment scope filter; analytics/charts; per-row re-run or diff.
