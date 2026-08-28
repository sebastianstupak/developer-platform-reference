# Machine Secret Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `POST …/secrets/export` returning an environment's secrets as a `name → value` JSON map, so apps can pull all their config in one authenticated, audited call.

**Architecture:** A new audited `ExportSecretsCommand` + handler that `ListAsync`-es the environment's secrets and decrypts each with its own retained `KeyId`. Exposed on the existing authorized secrets endpoint group (JWT or `Bearer dpk_…` API key via the "Smart" scheme); the dispatch pipeline enforces `SecretsRead` + environment scope for either principal.

**Tech Stack:** .NET 10, custom CQRS (`ICommand`/`ICommandHandler` + `ICommandDispatcher`), EF Core 9.x + Pomelo/MariaDB, xUnit (InMemory `ApplicationDbContext` + `WebApplicationFactory<Program>`).

## Global Constraints

- Clean Architecture; deps point inward to `Domain`; `DeveloperPlatform.ArchitectureTests` enforces boundaries.
- CQRS handlers are registered **manually** in `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs` (no assembly scanning); every new handler needs a line in the secrets block.
- The `CommandDispatcher` owns the unit of work (`SaveChanges`) and writes the audit outbox entry for every non-`[SkipAudit]` command. Handlers do not call `SaveChanges`.
- Permission: reuse `Permission.SecretsRead`. Scope: `Scope.Environment(EnvironmentId)`. No new permission values.
- The command carries only ids (no secret value), so `SensitiveDataScrubber` has nothing to redact; the result's values are never serialized to audit (same as `RevealSecretResult`).
- Each secret decrypts with its **own** `KeyId` (`crypto.DecryptAsync(tenantId, s.EncryptedValue, s.KeyId)`), so export works across key rotations via retained keys.
- Response is a JSON `name → value` object only. Empty environment → `{}`. No `.env`, no pagination (YAGNI).
- Commit hooks (lefthook pre-commit, ~1 min): `dotnet format developer-platform-reference.slnx --verify-no-changes` (run `dotnet format developer-platform-reference.slnx` BEFORE committing — new files may be LF, repo wants CRLF), `dotnet build -warnaserror` (zero warnings), architecture tests. commit-msg lint = Conventional Commits, NO AI co-author trailers. Never `--no-verify`. Use a 300000 ms timeout on the commit command.
- Solution file: `developer-platform-reference.slnx` (no `.sln`).
- Test harness: InMemory `ApplicationDbContext` + `new TenantCryptoService(_db, Key)` + a `TestExecutionContext : IExecutionContext` (see `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`). Authorization tests build a real `CommandDispatcher` (see `tests/DeveloperPlatform.Api.Tests/Secrets/SecretAuthorizationTests.cs`). Endpoint 401 tests use `WebApplicationFactory<Program>` and require `docker compose up -d db rabbitmq redis` (the host connects to RabbitMQ at startup); see `tests/DeveloperPlatform.Api.Tests/Projects/ProjectsAuthorizationTests.cs`.

---

## File Structure

- **Create** `src/DeveloperPlatform.Application/Secrets/ExportSecrets/ExportSecretsCommand.cs` — command + result.
- **Create** `src/DeveloperPlatform.Infrastructure/Secrets/ExportSecretsCommandHandler.cs` — list + decrypt-all.
- **Modify** `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs` — register the handler.
- **Modify** `src/DeveloperPlatform.Api/Endpoints/Secrets/SecretsEndpoints.cs` — the endpoint.
- **Modify** `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs` — handler tests.
- **Create** `tests/DeveloperPlatform.Api.Tests/Secrets/SecretExportAuthorizationTests.cs` — dispatcher-level authz + audit.
- **Create** `tests/DeveloperPlatform.Api.Tests/Secrets/SecretExportEndpointTests.cs` — 401 endpoint test.

---

### Task 1: Command, handler, registration + handler tests

**Files:**
- Create: `src/DeveloperPlatform.Application/Secrets/ExportSecrets/ExportSecretsCommand.cs`
- Create: `src/DeveloperPlatform.Infrastructure/Secrets/ExportSecretsCommandHandler.cs`
- Modify: `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`

**Interfaces:**
- Consumes: `ISecretRepository.ListAsync(Guid environmentId, CancellationToken)` → `Task<IReadOnlyList<Secret>>` (existing); `ITenantCryptoService.DecryptAsync(Guid tenantId, byte[] payload, Guid keyId, CancellationToken)` (existing); `IExecutionContext.TenantId` (existing).
- Produces:
  - `ExportSecretsCommand(Guid ProjectId, Guid EnvironmentId) : ICommand<ExportSecretsResult>, IResourceScoped` with `[RequiresPermission(Permission.SecretsRead)]`, `ResourceScope => Scope.Environment(EnvironmentId)`.
  - `ExportSecretsResult(IReadOnlyDictionary<string, string> Secrets)`.
  - `ExportSecretsCommandHandler` implementing `ICommandHandler<ExportSecretsCommand, ExportSecretsResult>`.

- [ ] **Step 1: Write the failing handler tests** — add to `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`:

```csharp
[Fact]
public async Task Export_Returns_All_Secrets_Decrypted()
{
    var crypto = new TenantCryptoService(_db, Key);
    await crypto.CreateKeyAsync(_tenant);
    await _db.SaveChangesAsync();
    var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
    var ctx = new TestExecutionContext { TenantId = _tenant };
    var set = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(repo, crypto, ctx);
    await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "DATABASE_URL", "postgres://x"));
    await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "API_KEY", "sk-123"));
    await _db.SaveChangesAsync();

    var handler = new DeveloperPlatform.Infrastructure.Secrets.ExportSecretsCommandHandler(repo, crypto, ctx);
    var result = await handler.HandleAsync(
        new DeveloperPlatform.Application.Secrets.ExportSecrets.ExportSecretsCommand(_project, _env));

    Assert.Equal(2, result.Secrets.Count);
    Assert.Equal("postgres://x", result.Secrets["DATABASE_URL"]);
    Assert.Equal("sk-123", result.Secrets["API_KEY"]);
}

[Fact]
public async Task Export_Empty_Environment_Returns_Empty()
{
    var crypto = new TenantCryptoService(_db, Key);
    await crypto.CreateKeyAsync(_tenant);
    await _db.SaveChangesAsync();
    var handler = new DeveloperPlatform.Infrastructure.Secrets.ExportSecretsCommandHandler(
        new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db), crypto,
        new TestExecutionContext { TenantId = _tenant });
    var result = await handler.HandleAsync(
        new DeveloperPlatform.Application.Secrets.ExportSecrets.ExportSecretsCommand(_project, _env));
    Assert.Empty(result.Secrets);
}

[Fact]
public async Task Export_Decrypts_Secrets_On_Retained_Older_Keys()
{
    var crypto = new TenantCryptoService(_db, Key);
    await crypto.CreateKeyAsync(_tenant);            // key A active
    await _db.SaveChangesAsync();
    var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
    var ctx = new TestExecutionContext { TenantId = _tenant };
    var set = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(repo, crypto, ctx);
    await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "OLD", "old-value"));  // on key A
    await _db.SaveChangesAsync();

    await crypto.CreateKeyAsync(_tenant);            // key B now newest/active; OLD stays on key A
    await _db.SaveChangesAsync();
    await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "NEW", "new-value"));  // on key B
    await _db.SaveChangesAsync();

    var handler = new DeveloperPlatform.Infrastructure.Secrets.ExportSecretsCommandHandler(repo, crypto, ctx);
    var result = await handler.HandleAsync(
        new DeveloperPlatform.Application.Secrets.ExportSecrets.ExportSecretsCommand(_project, _env));

    Assert.Equal("old-value", result.Secrets["OLD"]);   // key A retained → still decrypts
    Assert.Equal("new-value", result.Secrets["NEW"]);   // key B
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Export_Returns_All_Secrets_Decrypted|FullyQualifiedName~Export_Empty_Environment_Returns_Empty|FullyQualifiedName~Export_Decrypts_Secrets_On_Retained_Older_Keys"`
Expected: FAIL — `ExportSecretsCommand`/`ExportSecretsCommandHandler` do not exist (compile errors).

- [ ] **Step 3: Create the command** — `src/DeveloperPlatform.Application/Secrets/ExportSecrets/ExportSecretsCommand.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.ExportSecrets;

[RequiresPermission(Permission.SecretsRead)]
public record ExportSecretsCommand(Guid ProjectId, Guid EnvironmentId)
    : ICommand<ExportSecretsResult>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}

public record ExportSecretsResult(IReadOnlyDictionary<string, string> Secrets);
```

- [ ] **Step 4: Create the handler** — `src/DeveloperPlatform.Infrastructure/Secrets/ExportSecretsCommandHandler.cs`:

```csharp
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.ExportSecrets;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class ExportSecretsCommandHandler(
    ISecretRepository repository, ITenantCryptoService crypto, IExecutionContext ctx)
    : ICommandHandler<ExportSecretsCommand, ExportSecretsResult>
{
    public async Task<ExportSecretsResult> HandleAsync(ExportSecretsCommand command, CancellationToken ct = default)
    {
        var secrets = await repository.ListAsync(command.EnvironmentId, ct);
        var map = new Dictionary<string, string>(secrets.Count);
        foreach (var s in secrets)
        {
            map[s.Name] = await crypto.DecryptAsync(ctx.TenantId, s.EncryptedValue, s.KeyId, ct);
        }

        return new ExportSecretsResult(map);
    }
}
```

- [ ] **Step 5: Register the handler** — in `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs`, add to the secrets block (after the other secret handler registrations):

```csharp
        services.AddScoped<ICommandHandler<ExportSecretsCommand, ExportSecretsResult>, ExportSecretsCommandHandler>();
```

Add `using DeveloperPlatform.Application.Secrets.ExportSecrets;` at the top of the file if not already present.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Secrets.SecretTests"`
Expected: PASS (the three new tests plus the existing `SecretTests`).

- [ ] **Step 7: Commit**

```bash
git add src/DeveloperPlatform.Application/Secrets/ExportSecrets src/DeveloperPlatform.Infrastructure/Secrets/ExportSecretsCommandHandler.cs src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs
git commit -m "feat(secrets): export all environment secrets as a name-value map"
```

---

### Task 2: Authorization + audit (dispatcher-level)

**Files:**
- Create: `tests/DeveloperPlatform.Api.Tests/Secrets/SecretExportAuthorizationTests.cs`

**Interfaces:**
- Consumes: `ExportSecretsCommand`/`ExportSecretsResult` and `ExportSecretsCommandHandler` (Task 1); the `CommandDispatcher` wiring pattern from `SecretAuthorizationTests.cs`.

*Purpose:* prove the command's `[RequiresPermission(SecretsRead)]` + environment scope are enforced through the real dispatch pipeline for **both** a Member and a **ServiceAccount** principal (the machine/API-key path resolves to a ServiceAccount principal), and that an authorized export is audited.

- [ ] **Step 1: Write the failing tests** — create `tests/DeveloperPlatform.Api.Tests/Secrets/SecretExportAuthorizationTests.cs`:

```csharp
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.ExportSecrets;
using DeveloperPlatform.Application.Secrets.SetSecret;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Audit;
using DeveloperPlatform.Infrastructure.Context;
using DeveloperPlatform.Infrastructure.Crypto;
using DeveloperPlatform.Infrastructure.Dispatching;
using DeveloperPlatform.Infrastructure.Persistence;
using DeveloperPlatform.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Api.Tests.Secrets;

public class SecretExportAuthorizationTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _principal = Guid.NewGuid();
    private readonly Guid _project = Guid.NewGuid();
    private readonly Guid _env = Guid.NewGuid();
    private static readonly byte[] Key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    public async Task InitializeAsync()
    {
        var boot = new HttpExecutionContext { TenantId = _tenant, IpAddress = "127.0.0.1", PrincipalId = _principal, PrincipalType = PrincipalType.Member };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        _db = new ApplicationDbContext(options, boot, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // Build a dispatcher bound to a principal of the given type. Authorization resolves by
    // principal id + scope (type-agnostic), so this exercises both the Member and the
    // ServiceAccount (API-key) principal through the identical gate.
    private (CommandDispatcher Dispatcher, HttpExecutionContext Ctx) Build(PrincipalType type)
    {
        var ctx = new HttpExecutionContext { TenantId = _tenant, IpAddress = "127.0.0.1", PrincipalId = _principal, PrincipalType = type };
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<ExportSecretsCommand, ExportSecretsResult>, ExportSecretsCommandHandler>();
        services.AddScoped<ICommandHandler<SetSecretCommand, Unit>, SetSecretCommandHandler>();
        services.AddScoped<ISecretRepository, SecretRepository>();
        services.AddScoped(_ => _db);
        services.AddScoped<IExecutionContext>(_ => ctx);
        services.AddScoped<ITenantCryptoService>(_ => new TenantCryptoService(_db, Key));
        var sp = services.BuildServiceProvider();
        var authz = new DeveloperPlatform.Infrastructure.Authorization.AuthorizationService(_db);
        var dispatcher = new CommandDispatcher(sp, _db, ctx, new TenantCryptoService(_db, Key),
            new AuditOutboxRepository(_db), new SensitiveDataScrubber(), TenancyMode.SharedTables, authz);
        return (dispatcher, ctx);
    }

    private async Task GrantReadAsync()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsRead, Scope.Environment(_env)));
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Export_Forbidden_Without_Grant()
    {
        var (d, _) = Build(PrincipalType.Member);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            d.SendAsync<ExportSecretsCommand, ExportSecretsResult>(new ExportSecretsCommand(_project, _env)));
    }

    [Fact]
    public async Task Export_Allowed_For_Member_With_SecretsRead()
    {
        await GrantReadAsync();
        // Seed a secret via a write-capable member so there is something to export.
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsWrite, Scope.Environment(_env)));
        await _db.SaveChangesAsync();
        var (d, _) = Build(PrincipalType.Member);
        await d.SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "v"));

        var result = await d.SendAsync<ExportSecretsCommand, ExportSecretsResult>(new ExportSecretsCommand(_project, _env));
        Assert.Equal("v", result.Secrets["K"]);
    }

    [Fact]
    public async Task Export_Allowed_For_ServiceAccount_With_SecretsRead()
    {
        await GrantReadAsync();
        // Seed a secret directly (crypto + repository) so no write grant is needed.
        var crypto = new TenantCryptoService(_db, Key);
        var (payload, keyId) = await crypto.EncryptAsync(_tenant, "machine-value");
        _db.Secrets.Add(DeveloperPlatform.Domain.Secrets.Secret.Create(_tenant, _project, _env, "M", payload, keyId));
        await _db.SaveChangesAsync();

        var (d, _) = Build(PrincipalType.ServiceAccount);
        var result = await d.SendAsync<ExportSecretsCommand, ExportSecretsResult>(new ExportSecretsCommand(_project, _env));
        Assert.Equal("machine-value", result.Secrets["M"]);
    }

    [Fact]
    public async Task Export_Forbidden_For_ServiceAccount_Without_Grant()
    {
        var (d, _) = Build(PrincipalType.ServiceAccount);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            d.SendAsync<ExportSecretsCommand, ExportSecretsResult>(new ExportSecretsCommand(_project, _env)));
    }

    [Fact]
    public async Task Export_Writes_Audit_Entry()
    {
        await GrantReadAsync();
        var (d, _) = Build(PrincipalType.ServiceAccount);
        await d.SendAsync<ExportSecretsCommand, ExportSecretsResult>(new ExportSecretsCommand(_project, _env));

        var types = await _db.AuditOutboxEntries.AsNoTracking().Select(e => e.CommandType).ToListAsync();
        Assert.Contains(nameof(ExportSecretsCommand), types);
    }
}
```

- [ ] **Step 2: Run them to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~SecretExportAuthorizationTests"`
Expected: PASS (all five). If `Export_Forbidden_*` do not throw, the command is missing `[RequiresPermission]`/`IResourceScoped` — fix Task 1 before continuing.

*Note:* these are new tests over code already built in Task 1; there is no separate "make it fail then pass" cycle. The forbidden-path tests are the meaningful RED-equivalent: they would fail if the authorization attribute/scope were absent.

- [ ] **Step 3: Commit**

```bash
git add tests/DeveloperPlatform.Api.Tests/Secrets/SecretExportAuthorizationTests.cs
git commit -m "test(secrets): authz + audit coverage for secret export (member and service account)"
```

---

### Task 3: API endpoint + 401 test

**Files:**
- Modify: `src/DeveloperPlatform.Api/Endpoints/Secrets/SecretsEndpoints.cs`
- Create: `tests/DeveloperPlatform.Api.Tests/Secrets/SecretExportEndpointTests.cs`

**Interfaces:**
- Consumes: `ExportSecretsCommand`/`ExportSecretsResult` (Task 1); `ICommandDispatcher.SendAsync<TCommand, TResult>` (existing).
- Produces: `POST /api/v1/projects/{projectId:guid}/environments/{environmentId:guid}/secrets/export` returning `200 OK` with a JSON `name → value` object.

- [ ] **Step 1: Write the failing endpoint test** — create `tests/DeveloperPlatform.Api.Tests/Secrets/SecretExportEndpointTests.cs` (requires `docker compose up -d db rabbitmq redis`):

```csharp
using System.Net;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DeveloperPlatform.Api.Tests.Secrets;

public sealed class SecretExportEndpointTests : IClassFixture<SecretExportEndpointTests.DevPlatformFactory>
{
    private readonly DevPlatformFactory _factory;
    public SecretExportEndpointTests(DevPlatformFactory factory) => _factory = factory;

    [Fact]
    public async Task Export_Returns_401_Without_Auth()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.PostAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/environments/{Guid.NewGuid()}/secrets/export", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public sealed class DevPlatformFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.AddDbContext<ApplicationDbContext>((sp, opts) =>
                    opts.UseInMemoryDatabase("secret-export-endpoint-tests")
                        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                services.RemoveAll<IHostedService>();
            });
        }
    }
}
```

- [ ] **Step 2: Run it to verify it fails** (services must be up)

```bash
docker compose up -d db rabbitmq redis
dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~SecretExportEndpointTests"
```
Expected: FAIL — the `/export` route does not exist yet, so the request 404s instead of 401.

- [ ] **Step 3: Add the endpoint** — in `src/DeveloperPlatform.Api/Endpoints/Secrets/SecretsEndpoints.cs`, add the using at the top:

```csharp
using DeveloperPlatform.Application.Secrets.ExportSecrets;
```

Inside `MapSecrets`, after the existing `POST /{name}/reveal` mapping (still inside the `group`), add:

```csharp
        group.MapPost("/export", async (Guid projectId, Guid environmentId, ICommandDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync<ExportSecretsCommand, ExportSecretsResult>(
                new ExportSecretsCommand(projectId, environmentId), ct);
            return Results.Ok(result.Secrets);
        }).WithName("ExportSecrets").Produces<IReadOnlyDictionary<string, string>>();
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~SecretExportEndpointTests"`
Expected: PASS (401 without auth).

- [ ] **Step 5: Commit**

```bash
git add src/DeveloperPlatform.Api/Endpoints/Secrets/SecretsEndpoints.cs tests/DeveloperPlatform.Api.Tests/Secrets/SecretExportEndpointTests.cs
git commit -m "feat(secrets): expose POST /secrets/export endpoint"
```

---

## Final verification

- [ ] Run the whole suite: `dotnet test developer-platform-reference.slnx -c Release` (services up). Expected: all green.
- [ ] Format check: `dotnet format developer-platform-reference.slnx --verify-no-changes`. Expected: no changes.

---

## Self-Review (completed during authoring)

**Spec coverage:**
- §4 command/result → Task 1.
- §5 handler (ListAsync + decrypt-all, own KeyId, registration) → Task 1.
- §6 endpoint (POST /export, JSON map, empty → `{}`) → Task 3 (empty-env behavior verified by the handler test in Task 1).
- §7 authorization (Member + ServiceAccount) + audit (one `ExportSecretsCommand` event) → Task 2.
- §8 testing: handler (all secrets, empty, retained-key) → Task 1; dispatcher authz both principals + audit → Task 2; 401 endpoint → Task 3.
- §3 decisions (JSON map only, both callers) → endpoint returns the map; both principals covered by Task 2.

**Placeholder scan:** none — every code step is complete.

**Type consistency:** `ExportSecretsCommand(ProjectId, EnvironmentId)`, `ExportSecretsResult(IReadOnlyDictionary<string,string> Secrets)`, `ExportSecretsCommandHandler`, and the `SendAsync<ExportSecretsCommand, ExportSecretsResult>` dispatch are used consistently across all tasks and the endpoint.

**Note on the retained-key test:** rotation re-encrypts current values under the new key, so to genuinely exercise retained keys the Task 1 test creates a newer key **without** rotating (leaving `OLD` on the prior key), then confirms export still decrypts it — this is the real retained-key path for current values.
