# API Keys & Service Accounts Implementation Plan (Authz Slice 4 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the coarse `ApiKey`/`ApiKeyScope` with real API keys: a `ServiceAccount` machine principal holding permission grants, rotatable `ApiKeyCredential`s (SHA-256 hash stored, prefix shown, expiry, revocation) that authenticate as it, and a DB-backed API-key authentication scheme so a `Bearer dpk_…` key drives the Slice 3 enforcement engine.

**Architecture:** An `ApiKeyCredential` belongs to a `ServiceAccount` (the Slice 2 machine `Principal`); the SA's `PermissionGrant`s define scope (key inherits — user-approved). A caller presents `Authorization: Bearer dpk_<secret>`; an ASP.NET **policy scheme** forwards by token prefix to either the existing Keycloak JWT handler (humans) or a new `ApiKeyAuthenticationHandler` (machines). The handler hashes the presented key, looks up a live credential **ignoring the tenant query filter** (the key determines the tenant), and emits `tenant_id` + `principal_id` + `principal_type` claims; `ExecutionContextMiddleware` uses those directly for machine callers (skipping human JIT resolution). Enforcement is unchanged — the SA's grants flow through `IAuthorizationService`.

**Tech Stack:** .NET 10, EF Core + MySQL, ASP.NET Core auth (JWT + custom scheme), xUnit + EF InMemory.

## Where this fits (authorization subsystem roadmap)

**Slice 4 of 6.** Spec: `docs/specs/2026-08-25-permissions-member-management-design.md` (§3 ApiKeyCredential, §5 machine principal, §10 replace ApiKey). Depends on Slices 1-3 (merged): permission catalog, principal/grant model + `ServiceAccount`/`PermissionGrant`, enforcement + `IExecutionContext.PrincipalId`.

1. Catalog ✅  2. Model+seed ✅  3. Enforcement ✅  4. **API keys + service accounts** ← this plan  5. Member invitations + onboarding + grant endpoints  6. Web Access UI

**Deferred (documented):** the privilege-escalation guard (you can only grant what you hold) is Slice 5 — for now, creating a service account with grants requires `serviceaccounts:manage` but does not verify the actor holds those permissions. Per-key downscoping is not built (keys inherit the SA's grants). `TenantEncryptionKey` provisioning at onboarding is Slice 5 (unrelated to keys).

## Global Constraints

- `net10.0`; `Nullable` + `ImplicitUsings`. No type named `Action`.
- Layering: entities in Domain; contracts/attributes in Application; EF/handlers/auth in Infrastructure; endpoints/wiring in Api. Domain has no outward deps.
- **API key format:** `dpk_` + base64url(32 random bytes). Store ONLY `SHA-256` hex of the full key. `KeyPrefix` = the first 12 chars (`dpk_` + 8 secret chars), shown in listings; plaintext returned **once** at creation.
- **Machine principal:** an API-key request resolves to its `ServiceAccount`'s `Principal.Id` (`PrincipalType.ServiceAccount`); the SA's tenant is derived from the credential, NOT a claim the caller controls.
- **Build is `-warnaserror`** (pre-commit): no unused usings/warnings. `.gitattributes` mandates **CRLF** for `*.cs`; strip BOM on generated files. `commit-msg` hook REJECTS AI co-author trailers. Never `--no-verify`.
- **Migrations need MySQL:** `docker compose up -d db` (healthy); run `dotnet ef` with `--project src/DeveloperPlatform.Infrastructure --startup-project src/DeveloperPlatform.Infrastructure` and a `127.0.0.1` connection string (`localhost` times out on Windows/IPv6).
- Test framework xUnit; new tests under `tests/DeveloperPlatform.Api.Tests/Authorization/`.

---

## File Structure

**Created — Domain:** `src/DeveloperPlatform.Domain/ApiKeys/ApiKeyCredential.cs`
**Created — Application:**
- `src/DeveloperPlatform.Application/ServiceAccounts/CreateServiceAccount/CreateServiceAccountCommand.cs`
- `src/DeveloperPlatform.Application/ApiKeys/IssueApiKey/IssueApiKeyCommand.cs`
- `src/DeveloperPlatform.Application/ApiKeys/RevokeApiKey/RevokeApiKeyCommand.cs`
- `src/DeveloperPlatform.Application/ApiKeys/GetApiKeys/GetApiKeysQuery.cs`
- `src/DeveloperPlatform.Application/Authorization/GrantSpec.cs` (a `(Permission, ScopeType, Guid?)` DTO for creation)
**Created — Infrastructure:**
- `src/DeveloperPlatform.Infrastructure/Persistence/Configurations/ApiKeyCredentialConfiguration.cs`
- `src/DeveloperPlatform.Infrastructure/ApiKeys/CreateServiceAccountCommandHandler.cs`
- `src/DeveloperPlatform.Infrastructure/ApiKeys/IssueApiKeyCommandHandler.cs`
- `src/DeveloperPlatform.Infrastructure/ApiKeys/RevokeApiKeyCommandHandler.cs`
- `src/DeveloperPlatform.Infrastructure/ApiKeys/GetApiKeysQueryHandler.cs`
- `src/DeveloperPlatform.Infrastructure/Authorization/ApiKeyAuthenticationHandler.cs`
**Created — Api:**
- `src/DeveloperPlatform.Api/Endpoints/ServiceAccounts/ServiceAccountsEndpoints.cs`
- `src/DeveloperPlatform.Api/Endpoints/ApiKeys/ApiKeysEndpoints.cs`
**Modified:** `ApplicationDbContext.cs` (DbSet), `ServiceCollectionExtensions.cs` (register handlers), `ExecutionContextMiddleware.cs` (machine principal), `Program.cs` (policy scheme + endpoints).
**Removed (Task 5):** `Domain/ApiKeys/ApiKey.cs`, `ApiKeyScope.cs`; `Configurations/ApiKeyConfiguration.cs`; `Infrastructure/ApiKeys/{ApiKeyRepository,IApiKeyRepository,CreateApiKeyCommandHandler}.cs`; `Application/ApiKeys/CreateApiKey/CreateApiKeyCommand.cs`; `Api/Endpoints/ApiKeys/CreateApiKeyEndpoint.cs`; `tests/.../ApiKeys/CreateApiKeyTests.cs`.
**Tests (new):** `ApiKeyCredentialTests.cs`, `ApiKeyAuthenticationHandlerTests.cs`, `IssueApiKeyTests.cs`.

---

## Task 1: `ApiKeyCredential` entity + EF config + migration (additive)

**Files:** Create `src/DeveloperPlatform.Domain/ApiKeys/ApiKeyCredential.cs`, `src/DeveloperPlatform.Infrastructure/Persistence/Configurations/ApiKeyCredentialConfiguration.cs`; modify `ApplicationDbContext.cs`; Test `tests/DeveloperPlatform.Api.Tests/Authorization/ApiKeyCredentialTests.cs`.

**Interfaces:**
- Produces: `ApiKeyCredential.Create(Guid tenantId, Guid serviceAccountId, string name, string keyPrefix, string keyHash, DateTime? expiresAt)` → `ApiKeyCredential { Id, TenantId, Guid ServiceAccountId, string Name, string KeyPrefix, string KeyHash, DateTime? ExpiresAt, bool IsRevoked, DateTime? RevokedAt, DateTime? LastUsedAt }`; methods `Revoke()`, `RecordUsage()`, `bool IsActive(DateTime nowUtc)`.

- [ ] **Step 1: Write the failing entity test**

Create `tests/DeveloperPlatform.Api.Tests/Authorization/ApiKeyCredentialTests.cs`:

```csharp
using DeveloperPlatform.Domain.ApiKeys;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class ApiKeyCredentialTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Sa = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ApiKeyCredential New(DateTime? expiresAt) =>
        ApiKeyCredential.Create(Tenant, Sa, "ci", "dpk_abc12345", "HASH", expiresAt);

    [Fact]
    public void Create_Sets_Fields_And_Requires_Name()
    {
        var c = New(null);
        Assert.Equal(Sa, c.ServiceAccountId);
        Assert.Equal("dpk_abc12345", c.KeyPrefix);
        Assert.Equal("HASH", c.KeyHash);
        Assert.False(c.IsRevoked);
        Assert.Throws<ArgumentException>(() => ApiKeyCredential.Create(Tenant, Sa, " ", "p", "h", null));
    }

    [Fact]
    public void IsActive_Honours_Revocation_And_Expiry()
    {
        Assert.True(New(null).IsActive(Now));                       // no expiry, not revoked
        Assert.True(New(Now.AddDays(1)).IsActive(Now));             // not yet expired
        Assert.False(New(Now.AddDays(-1)).IsActive(Now));           // expired

        var revoked = New(null);
        revoked.Revoke();
        Assert.True(revoked.IsRevoked);
        Assert.NotNull(revoked.RevokedAt);
        Assert.False(revoked.IsActive(Now));
    }

    [Fact]
    public void RecordUsage_Sets_LastUsedAt()
    {
        var c = New(null);
        Assert.Null(c.LastUsedAt);
        c.RecordUsage();
        Assert.NotNull(c.LastUsedAt);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~ApiKeyCredentialTests"`
Expected: FAIL — `ApiKeyCredential` does not exist.

- [ ] **Step 3: Create the entity**

`src/DeveloperPlatform.Domain/ApiKeys/ApiKeyCredential.cs`:

```csharp
using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.ApiKeys;

// A rotatable API-key credential that authenticates AS a ServiceAccount principal.
// Only a SHA-256 hash of the key is stored; the plaintext is shown once at creation.
public class ApiKeyCredential : TenantEntity
{
    public Guid ServiceAccountId { get; private set; }   // → Principal.Id (Type = ServiceAccount)
    public string Name { get; private set; } = string.Empty;
    public string KeyPrefix { get; private set; } = string.Empty;   // "dpk_" + first 8 secret chars, shown in listings
    public string KeyHash { get; private set; } = string.Empty;     // SHA-256 hex of the full key
    public DateTime? ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }

    private ApiKeyCredential() { }

    public static ApiKeyCredential Create(
        Guid tenantId, Guid serviceAccountId, string name, string keyPrefix, string keyHash, DateTime? expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyHash);
        return new ApiKeyCredential
        {
            TenantId = tenantId,
            ServiceAccountId = serviceAccountId,
            Name = name,
            KeyPrefix = keyPrefix,
            KeyHash = keyHash,
            ExpiresAt = expiresAt
        };
    }

    public bool IsActive(DateTime nowUtc) =>
        !IsRevoked && (ExpiresAt is null || ExpiresAt > nowUtc);

    public void Revoke()
    {
        IsRevoked = true;
        RevokedAt = DateTime.UtcNow;
    }

    public void RecordUsage() => LastUsedAt = DateTime.UtcNow;
}
```

- [ ] **Step 4: Add the DbSet + config**

In `src/DeveloperPlatform.Infrastructure/Persistence/ApplicationDbContext.cs`, add after the existing `Invitations` DbSet:

```csharp
    public DbSet<ApiKeyCredential> ApiKeyCredentials => Set<ApiKeyCredential>();
```

(the `using DeveloperPlatform.Domain.ApiKeys;` already exists for `ApiKey` — keep it.)

`src/DeveloperPlatform.Infrastructure/Persistence/Configurations/ApiKeyCredentialConfiguration.cs`:

```csharp
using DeveloperPlatform.Domain.ApiKeys;
using DeveloperPlatform.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class ApiKeyCredentialConfiguration : IEntityTypeConfiguration<ApiKeyCredential>
{
    public void Configure(EntityTypeBuilder<ApiKeyCredential> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.KeyPrefix).HasMaxLength(20).IsRequired();
        builder.Property(c => c.KeyHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(c => c.TenantId);
        builder.HasIndex(c => c.ServiceAccountId);
        builder.HasIndex(c => c.KeyHash).IsUnique();   // auth looks keys up by hash

        builder.HasOne<Principal>().WithMany().HasForeignKey(c => c.ServiceAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 5: Run the entity test**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~ApiKeyCredentialTests"`
Expected: PASS (3 tests). Then `dotnet build developer-platform-reference.slnx --no-restore` → 0 errors.

- [ ] **Step 6: Generate the migration**

`docker compose up -d db` (wait healthy), then with a `127.0.0.1` connection:
Run: `dotnet ef migrations add AddApiKeyCredentials --project src/DeveloperPlatform.Infrastructure --startup-project src/DeveloperPlatform.Infrastructure`
Verify: `grep -oE 'CreateTable\(\s*name: "ApiKeyCredentials"' src/DeveloperPlatform.Infrastructure/Migrations/*_AddApiKeyCredentials.cs` prints one match. Strip BOM if `dotnet format` complains. Build again → 0 errors.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(authz): ApiKeyCredential entity, config, and migration"
```

---

## Task 2: Service-account + API-key application layer

**Files:** Create the 4 Application command/query records + `GrantSpec.cs`; the 4 Infrastructure handlers; Test `tests/DeveloperPlatform.Api.Tests/Authorization/IssueApiKeyTests.cs`. Modify `ServiceCollectionExtensions.cs` (register). 

**Interfaces:**
- Consumes: `Principal`, `ServiceAccount`, `PermissionGrant`, `ApiKeyCredential`, `Permission`, `Scope` (Slices 1-2, Task 1); `IExecutionContext`.
- Produces:
  - `GrantSpec(Permission Permission, ScopeType ScopeType, Guid? ScopeTargetId)`
  - `CreateServiceAccountCommand(string Name, string? Description, IReadOnlyList<GrantSpec> Grants) : ICommand<CreateServiceAccountResult>` → `CreateServiceAccountResult(Guid ServiceAccountId)` — guarded `[RequiresPermission(Permission.ServiceAccountsManage)]`.
  - `IssueApiKeyCommand(Guid ServiceAccountId, string Name, DateTime? ExpiresAt) : ICommand<IssueApiKeyResult>, IResourceScoped` (ResourceScope = Tenant) → `IssueApiKeyResult(Guid CredentialId, string PlaintextKey, string KeyPrefix)` — guarded `[RequiresPermission(Permission.ApiKeysManage)]`.
  - `RevokeApiKeyCommand(Guid CredentialId) : ICommand` — guarded `[RequiresPermission(Permission.ApiKeysManage)]`.
  - `GetApiKeysQuery(Guid ServiceAccountId) : IQuery<IReadOnlyList<ApiKeySummary>>` (guarded `apikeys:manage`) → `ApiKeySummary(Guid Id, string Name, string KeyPrefix, DateTime? ExpiresAt, bool IsRevoked, DateTime? LastUsedAt, DateTime CreatedAt)`.

- [ ] **Step 1: Write the failing issue-key test**

Create `tests/DeveloperPlatform.Api.Tests/Authorization/IssueApiKeyTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~IssueApiKeyTests"`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Create the Application contracts**

`src/DeveloperPlatform.Application/Authorization/GrantSpec.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Authorization;

// A permission to grant at a scope, supplied when creating a service account.
public sealed record GrantSpec(Permission Permission, ScopeType ScopeType, Guid? ScopeTargetId);
```

`src/DeveloperPlatform.Application/ServiceAccounts/CreateServiceAccount/CreateServiceAccountCommand.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.ServiceAccounts.CreateServiceAccount;

[RequiresPermission(Permission.ServiceAccountsManage)]
public record CreateServiceAccountCommand(
    string Name, string? Description, IReadOnlyList<GrantSpec> Grants)
    : ICommand<CreateServiceAccountResult>;

public record CreateServiceAccountResult(Guid ServiceAccountId);
```

`src/DeveloperPlatform.Application/ApiKeys/IssueApiKey/IssueApiKeyCommand.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.ApiKeys.IssueApiKey;

[RequiresPermission(Permission.ApiKeysManage)]
public record IssueApiKeyCommand(Guid ServiceAccountId, string Name, DateTime? ExpiresAt)
    : ICommand<IssueApiKeyResult>, IResourceScoped
{
    public Scope ResourceScope => Scope.Tenant;
}

public record IssueApiKeyResult(Guid CredentialId, string PlaintextKey, string KeyPrefix);
```

`src/DeveloperPlatform.Application/ApiKeys/RevokeApiKey/RevokeApiKeyCommand.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.ApiKeys.RevokeApiKey;

[RequiresPermission(Permission.ApiKeysManage)]
public record RevokeApiKeyCommand(Guid CredentialId) : ICommand;
```

`src/DeveloperPlatform.Application/ApiKeys/GetApiKeys/GetApiKeysQuery.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.ApiKeys.GetApiKeys;

[RequiresPermission(Permission.ApiKeysManage)]
public record GetApiKeysQuery(Guid ServiceAccountId) : IQuery<IReadOnlyList<ApiKeySummary>>;

public record ApiKeySummary(
    Guid Id, string Name, string KeyPrefix, DateTime? ExpiresAt,
    bool IsRevoked, DateTime? LastUsedAt, DateTime CreatedAt);
```

- [ ] **Step 4: Implement the handlers**

`src/DeveloperPlatform.Infrastructure/ApiKeys/CreateServiceAccountCommandHandler.cs`:

```csharp
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.ServiceAccounts.CreateServiceAccount;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;

namespace DeveloperPlatform.Infrastructure.ApiKeys;

public sealed class CreateServiceAccountCommandHandler(
    ApplicationDbContext db, IExecutionContext executionContext)
    : ICommandHandler<CreateServiceAccountCommand, CreateServiceAccountResult>
{
    public async Task<CreateServiceAccountResult> HandleAsync(
        CreateServiceAccountCommand command, CancellationToken ct = default)
    {
        var tenantId = executionContext.TenantId;
        var principal = Principal.CreateServiceAccount(tenantId, command.Name);
        db.Principals.Add(principal);
        db.ServiceAccounts.Add(ServiceAccount.Create(tenantId, principal.Id, command.Name, command.Description));

        foreach (var g in command.Grants)
        {
            var scope = Scope.Create(g.ScopeType, g.ScopeTargetId);
            db.PermissionGrants.Add(PermissionGrant.Create(tenantId, principal.Id, g.Permission, scope));
        }

        await db.SaveChangesAsync(ct);
        return new CreateServiceAccountResult(principal.Id);
    }
}
```

`src/DeveloperPlatform.Infrastructure/ApiKeys/IssueApiKeyCommandHandler.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using DeveloperPlatform.Application.ApiKeys.IssueApiKey;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Domain.ApiKeys;
using DeveloperPlatform.Infrastructure.Persistence;

namespace DeveloperPlatform.Infrastructure.ApiKeys;

public sealed class IssueApiKeyCommandHandler(
    ApplicationDbContext db, IExecutionContext executionContext)
    : ICommandHandler<IssueApiKeyCommand, IssueApiKeyResult>
{
    public async Task<IssueApiKeyResult> HandleAsync(IssueApiKeyCommand command, CancellationToken ct = default)
    {
        var rawBytes = RandomNumberGenerator.GetBytes(32);
        var plaintextKey = "dpk_" + Convert.ToBase64String(rawBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey)));
        var keyPrefix = plaintextKey[..12];   // "dpk_" + 8 chars, shown in listings

        var credential = ApiKeyCredential.Create(
            executionContext.TenantId, command.ServiceAccountId, command.Name,
            keyPrefix, keyHash, command.ExpiresAt);
        db.ApiKeyCredentials.Add(credential);
        await db.SaveChangesAsync(ct);

        return new IssueApiKeyResult(credential.Id, plaintextKey, keyPrefix);
    }
}
```

`src/DeveloperPlatform.Infrastructure/ApiKeys/RevokeApiKeyCommandHandler.cs`:

```csharp
using DeveloperPlatform.Application.ApiKeys.RevokeApiKey;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.ApiKeys;

public sealed class RevokeApiKeyCommandHandler(ApplicationDbContext db)
    : ICommandHandler<RevokeApiKeyCommand, Unit>
{
    public async Task<Unit> HandleAsync(RevokeApiKeyCommand command, CancellationToken ct = default)
    {
        var credential = await db.ApiKeyCredentials.FirstOrDefaultAsync(c => c.Id == command.CredentialId, ct)
            ?? throw new KeyNotFoundException($"API key credential {command.CredentialId} not found.");
        credential.Revoke();
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

`src/DeveloperPlatform.Infrastructure/ApiKeys/GetApiKeysQueryHandler.cs`:

```csharp
using DeveloperPlatform.Application.ApiKeys.GetApiKeys;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.ApiKeys;

public sealed class GetApiKeysQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetApiKeysQuery, IReadOnlyList<ApiKeySummary>>
{
    public async Task<IReadOnlyList<ApiKeySummary>> HandleAsync(GetApiKeysQuery query, CancellationToken ct = default)
    {
        return await db.ApiKeyCredentials.AsNoTracking()
            .Where(c => c.ServiceAccountId == query.ServiceAccountId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ApiKeySummary(
                c.Id, c.Name, c.KeyPrefix, c.ExpiresAt, c.IsRevoked, c.LastUsedAt, c.CreatedAt))
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 5: Register the handlers**

In `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs`, add the `using`s and register (next to the other handler registrations), replacing the OLD `CreateApiKeyCommandHandler`/`IApiKeyRepository` registration lines with:

```csharp
        services.AddScoped<ICommandHandler<CreateServiceAccountCommand, CreateServiceAccountResult>, CreateServiceAccountCommandHandler>();
        services.AddScoped<ICommandHandler<IssueApiKeyCommand, IssueApiKeyResult>, IssueApiKeyCommandHandler>();
        services.AddScoped<ICommandHandler<RevokeApiKeyCommand, Unit>, RevokeApiKeyCommandHandler>();
        services.AddScoped<IQueryHandler<GetApiKeysQuery, IReadOnlyList<ApiKeySummary>>, GetApiKeysQueryHandler>();
```

Add the matching `using DeveloperPlatform.Application.ServiceAccounts.CreateServiceAccount;`, `using DeveloperPlatform.Application.ApiKeys.IssueApiKey;`, `using DeveloperPlatform.Application.ApiKeys.RevokeApiKey;`, `using DeveloperPlatform.Application.ApiKeys.GetApiKeys;`. Leave the OLD `CreateApiKeyCommandHandler` registration in place for now (removed in Task 5) — or if removing it now causes no other break, keep it until Task 5 to avoid touching the old endpoint prematurely. **Keep the old registration; only ADD here.**

- [ ] **Step 6: Run the test + build**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~IssueApiKeyTests"` → PASS. Then `dotnet build developer-platform-reference.slnx --no-restore` → 0 errors/0 warnings.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(authz): service-account creation + API-key issue/revoke/list commands"
```

---

## Task 3: API-key authentication handler + machine principal

**Files:** Create `src/DeveloperPlatform.Infrastructure/Authorization/ApiKeyAuthenticationHandler.cs`; modify `ExecutionContextMiddleware.cs`, `Program.cs`; Test `tests/DeveloperPlatform.Api.Tests/Authorization/ApiKeyAuthenticationHandlerTests.cs`.

**Interfaces:**
- Consumes: `ApiKeyCredential`, `Principal` (Task 1); `ApplicationDbContext`.
- Produces: `ApiKeyAuthenticationHandler` (scheme "ApiKey") that on a valid `dpk_` key issues a `ClaimsPrincipal` with `tenant_id`, `principal_id`, `principal_type` claims; the middleware reads `principal_id` for machine callers.

- [ ] **Step 1: Write the failing handler test**

Create `tests/DeveloperPlatform.Api.Tests/Authorization/ApiKeyAuthenticationHandlerTests.cs`. This tests the credential-resolution logic via a small helper the handler exposes (`static (Guid principalId, Guid tenantId)? ResolveCredential(ApplicationDbContext db, string presentedKey)`), so it needs no ASP.NET host:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~ApiKeyAuthenticationHandlerTests"`
Expected: FAIL — `ApiKeyAuthenticationHandler` does not exist.

- [ ] **Step 3: Implement the handler**

`src/DeveloperPlatform.Infrastructure/Authorization/ApiKeyAuthenticationHandler.cs`:

```csharp
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeveloperPlatform.Infrastructure.Authorization;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApplicationDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string KeyPrefixMarker = "dpk_";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (!header.StartsWith(bearer, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }
        var key = header[bearer.Length..].Trim();
        if (!key.StartsWith(KeyPrefixMarker, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var resolved = await ResolveCredentialAsync(db, key, DateTime.UtcNow);
        if (resolved is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var claims = new[]
        {
            new Claim("tenant_id", resolved.Value.TenantId.ToString()),
            new Claim("principal_id", resolved.Value.PrincipalId.ToString()),
            new Claim("principal_type", nameof(PrincipalType.ServiceAccount)),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    // Looks a key up by its SHA-256 hash, IGNORING the tenant query filter (the key determines the tenant),
    // and returns the owning service-account principal + tenant if the credential is active.
    public static async Task<(Guid PrincipalId, Guid TenantId)?> ResolveCredentialAsync(
        ApplicationDbContext db, string presentedKey, DateTime nowUtc, CancellationToken ct = default)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey)));
        var credential = await db.ApiKeyCredentials
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.KeyHash == hash, ct);
        if (credential is null || !credential.IsActive(nowUtc))
        {
            return null;
        }

        credential.RecordUsage();
        await db.SaveChangesAsync(ct);
        return (credential.ServiceAccountId, credential.TenantId);
    }
}
```

- [ ] **Step 4: Machine principal in the middleware**

In `src/DeveloperPlatform.Infrastructure/Context/ExecutionContextMiddleware.cs`, before the human `IPrincipalResolver` call, short-circuit for machine callers carrying a `principal_id` claim (set by the API-key handler). Replace the resolver block (the `var resolver = …; var resolved = …; if (resolved is not null) {…}` section) with:

```csharp
        if (Guid.TryParse(httpContext.User.FindFirst("principal_id")?.Value, out var machinePrincipalId))
        {
            executionContext.PrincipalId = machinePrincipalId;
            executionContext.PrincipalType = DeveloperPlatform.Domain.Authorization.PrincipalType.ServiceAccount;
        }
        else
        {
            var resolver = httpContext.RequestServices.GetRequiredService<IPrincipalResolver>();
            var resolved = await resolver.ResolveAsync(httpContext.User, tenantId, httpContext.RequestAborted);
            if (resolved is not null)
            {
                executionContext.PrincipalId = resolved.PrincipalId;
                executionContext.PrincipalType = resolved.Type;
                executionContext.UserId = resolved.UserId;
            }
        }
```

- [ ] **Step 5: Register the auth scheme (policy scheme forwarding)**

In `src/DeveloperPlatform.Api/Program.cs`, replace the `builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` block with a policy scheme that forwards by token prefix. Change the opening line to `AddAuthentication("Smart")`, add the policy scheme, keep the existing `.AddJwtBearer(...)` exactly, and add the ApiKey scheme:

```csharp
    builder.Services.AddAuthentication("Smart")
        .AddPolicyScheme("Smart", "JWT or API key", options =>
        {
            options.ForwardDefaultSelector = ctx =>
            {
                var auth = ctx.Request.Headers.Authorization.ToString();
                return auth.StartsWith("Bearer " + DeveloperPlatform.Infrastructure.Authorization.ApiKeyAuthenticationHandler.KeyPrefixMarker, StringComparison.Ordinal)
                    ? DeveloperPlatform.Infrastructure.Authorization.ApiKeyAuthenticationHandler.SchemeName
                    : JwtBearerDefaults.AuthenticationScheme;
            };
        })
        .AddJwtBearer(options =>
        {
            options.Authority = builder.Configuration["Keycloak:Authority"];
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                NameClaimType = "preferred_username",
                RoleClaimType = "realm_access.roles"
            };
        })
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
                   DeveloperPlatform.Infrastructure.Authorization.ApiKeyAuthenticationHandler>(
            DeveloperPlatform.Infrastructure.Authorization.ApiKeyAuthenticationHandler.SchemeName, _ => { });
```

- [ ] **Step 6: Run the handler test + build**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~ApiKeyAuthenticationHandlerTests"` → PASS (3). Then `dotnet build developer-platform-reference.slnx --no-restore` → 0 errors/0 warnings.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(authz): API-key authentication scheme + machine principal resolution"
```

---

## Task 4: Service-account & API-key endpoints

**Files:** Create `src/DeveloperPlatform.Api/Endpoints/ServiceAccounts/ServiceAccountsEndpoints.cs`, `src/DeveloperPlatform.Api/Endpoints/ApiKeys/ApiKeysEndpoints.cs`; modify `Program.cs` (register).

**Interfaces:** Consumes the Task 2 commands/queries via `ICommandDispatcher`/`IQueryDispatcher`.

- [ ] **Step 1: Create the service-accounts endpoints**

`src/DeveloperPlatform.Api/Endpoints/ServiceAccounts/ServiceAccountsEndpoints.cs`:

```csharp
using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.ServiceAccounts.CreateServiceAccount;
using DeveloperPlatform.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeveloperPlatform.Api.Endpoints.ServiceAccounts;

public static class ServiceAccountsEndpoints
{
    public static IEndpointRouteBuilder MapServiceAccounts(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        app.MapPost("/api/v1/service-accounts", async (
            [FromBody] CreateServiceAccountRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var grants = (request.Grants ?? [])
                .Select(g => new GrantSpec(g.Permission, g.ScopeType, g.ScopeTargetId))
                .ToList();
            var result = await dispatcher.SendAsync<CreateServiceAccountCommand, CreateServiceAccountResult>(
                new CreateServiceAccountCommand(request.Name, request.Description, grants), ct);
            return Results.Created($"/api/v1/service-accounts/{result.ServiceAccountId}",
                new CreateServiceAccountResponse(result.ServiceAccountId));
        })
        .WithName("CreateServiceAccount").WithTags("Service Accounts")
        .WithSummary("Create a service account with permission grants")
        .Produces<CreateServiceAccountResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        return app;
    }

    public record CreateServiceAccountRequest(string Name, string? Description, List<GrantRequest>? Grants);
    public record GrantRequest(Permission Permission, ScopeType ScopeType, Guid? ScopeTargetId);
    public record CreateServiceAccountResponse(Guid ServiceAccountId);
}
```

- [ ] **Step 2: Create the api-keys endpoints**

`src/DeveloperPlatform.Api/Endpoints/ApiKeys/ApiKeysEndpoints.cs`:

```csharp
using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.ApiKeys.GetApiKeys;
using DeveloperPlatform.Application.ApiKeys.IssueApiKey;
using DeveloperPlatform.Application.ApiKeys.RevokeApiKey;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Queries;
using Microsoft.AspNetCore.Mvc;

namespace DeveloperPlatform.Api.Endpoints.ApiKeys;

public static class ApiKeysEndpoints
{
    public static IEndpointRouteBuilder MapApiKeys(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v1/service-accounts/{serviceAccountId:guid}/keys")
            .WithTags("API Keys").WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        group.MapPost("/", async (
            Guid serviceAccountId, [FromBody] IssueApiKeyRequest request,
            ICommandDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync<IssueApiKeyCommand, IssueApiKeyResult>(
                new IssueApiKeyCommand(serviceAccountId, request.Name, request.ExpiresAt), ct);
            return Results.Created(
                $"/api/v1/service-accounts/{serviceAccountId}/keys/{result.CredentialId}",
                new IssueApiKeyResponse(result.CredentialId, result.PlaintextKey, result.KeyPrefix));
        })
        .WithName("IssueApiKey").WithSummary("Issue an API key")
        .WithDescription("The plaintext key is returned **once**. Only a SHA-256 hash is stored.")
        .Produces<IssueApiKeyResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", async (
            Guid serviceAccountId, IQueryDispatcher dispatcher, CancellationToken ct) =>
        {
            var keys = await dispatcher.SendAsync<GetApiKeysQuery, IReadOnlyList<ApiKeySummary>>(
                new GetApiKeysQuery(serviceAccountId), ct);
            return Results.Ok(keys);
        })
        .WithName("GetApiKeys").WithSummary("List a service account's API keys (metadata only)")
        .Produces<IReadOnlyList<ApiKeySummary>>(StatusCodes.Status200OK);

        group.MapPost("/{credentialId:guid}/revoke", async (
            Guid credentialId, ICommandDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.SendAsync<RevokeApiKeyCommand, Unit>(new RevokeApiKeyCommand(credentialId), ct);
            return Results.NoContent();
        })
        .WithName("RevokeApiKey").WithSummary("Revoke an API key")
        .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public record IssueApiKeyRequest(string Name, DateTime? ExpiresAt);
    public record IssueApiKeyResponse(Guid CredentialId, string PlaintextKey, string KeyPrefix,
        string Warning = "Store this key — it cannot be shown again.");
}
```

- [ ] **Step 3: Register the endpoints**

In `src/DeveloperPlatform.Api/Program.cs`, add the usings and, next to the other `app.Map*` calls, add:

```csharp
    app.MapServiceAccounts(versionSet);
    app.MapApiKeys(versionSet);
```

(add `using DeveloperPlatform.Api.Endpoints.ServiceAccounts;` and `using DeveloperPlatform.Api.Endpoints.ApiKeys;`). Leave the OLD `app.MapCreateApiKey(versionSet);` for now — removed in Task 5.

- [ ] **Step 4: Build**

Run: `dotnet build developer-platform-reference.slnx --no-restore` → 0 errors/0 warnings.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(authz): service-account and API-key endpoints"
```

---

## Task 5: Remove the old `ApiKey` stack

**Files:** Delete `Domain/ApiKeys/ApiKey.cs`, `ApiKeyScope.cs`; `Configurations/ApiKeyConfiguration.cs`; `Infrastructure/ApiKeys/{ApiKeyRepository,IApiKeyRepository,CreateApiKeyCommandHandler}.cs`; `Application/ApiKeys/CreateApiKey/CreateApiKeyCommand.cs`; `Api/Endpoints/ApiKeys/CreateApiKeyEndpoint.cs`; `tests/.../ApiKeys/CreateApiKeyTests.cs`. Modify `ApplicationDbContext.cs`, `ServiceCollectionExtensions.cs`, `Program.cs`, `tests/.../Auth/ApiAuthorizationTests.cs`; generate a drop migration.

- [ ] **Step 1: Delete the old files**

```bash
git rm src/DeveloperPlatform.Domain/ApiKeys/ApiKey.cs \
       src/DeveloperPlatform.Domain/ApiKeys/ApiKeyScope.cs \
       src/DeveloperPlatform.Infrastructure/Persistence/Configurations/ApiKeyConfiguration.cs \
       src/DeveloperPlatform.Infrastructure/ApiKeys/ApiKeyRepository.cs \
       src/DeveloperPlatform.Infrastructure/ApiKeys/IApiKeyRepository.cs \
       src/DeveloperPlatform.Infrastructure/ApiKeys/CreateApiKeyCommandHandler.cs \
       src/DeveloperPlatform.Application/ApiKeys/CreateApiKey/CreateApiKeyCommand.cs \
       src/DeveloperPlatform.Api/Endpoints/ApiKeys/CreateApiKeyEndpoint.cs \
       tests/DeveloperPlatform.Api.Tests/ApiKeys/CreateApiKeyTests.cs
```

- [ ] **Step 2: Remove the dangling references**

- `src/DeveloperPlatform.Infrastructure/Persistence/ApplicationDbContext.cs`: delete the line `public DbSet<ApiKey> ApiKeys => Set<ApiKey>();` and, if now unused, the `using DeveloperPlatform.Domain.ApiKeys;` — but note `ApiKeyCredential` is in that same namespace and IS used, so KEEP the using.
- `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs`: delete the two lines registering `IApiKeyRepository`/`ApiKeyRepository` and `ICommandHandler<CreateApiKeyCommand, CreateApiKeyResult>`/`CreateApiKeyCommandHandler`, and the now-unused `using DeveloperPlatform.Application.ApiKeys.CreateApiKey;` / `using DeveloperPlatform.Infrastructure.ApiKeys;` **only if** no other symbol from those namespaces is used (the new handlers ARE in `DeveloperPlatform.Infrastructure.ApiKeys`, so keep that using).
- `src/DeveloperPlatform.Api/Program.cs`: delete `app.MapCreateApiKey(versionSet);` and the `using DeveloperPlatform.Api.Endpoints.ApiKeys;`? — no, `ApiKeysEndpoints` is in that namespace and IS used (`MapApiKeys`), so keep the using; just remove the `MapCreateApiKey` call and the now-unused `using ...Endpoints.ApiKeys;` is still needed. Also remove `using DeveloperPlatform.Api.Endpoints.ApiKeys;` duplication if any.
- `tests/DeveloperPlatform.Api.Tests/Auth/ApiAuthorizationTests.cs`: the `CreateApiKey_Returns_401_Without_Auth` test POSTs to the removed `/api/v1/projects/{id}/api-keys` route. Repoint it to a still-existing guarded route to keep the "401 without auth" assertion meaningful — change the request to `POST /api/v1/service-accounts` with body `new { name = "x" }` and keep the `Assert.Equal(HttpStatusCode.Unauthorized, …)`.

- [ ] **Step 3: Build to find remaining breaks**

Run: `dotnet build developer-platform-reference.slnx --no-restore`
Expected: 0 errors. If the compiler flags any remaining reference to `ApiKey`/`ApiKeyScope`/`CreateApiKeyCommand`/`MapCreateApiKey`, remove it (they are all listed above; there should be none left).

- [ ] **Step 4: Generate the drop migration**

`docker compose up -d db` (healthy). With `127.0.0.1`:
Run: `dotnet ef migrations add DropLegacyApiKeys --project src/DeveloperPlatform.Infrastructure --startup-project src/DeveloperPlatform.Infrastructure`
Verify: `grep -oE 'DropTable\(\s*name: "ApiKeys"' src/DeveloperPlatform.Infrastructure/Migrations/*_DropLegacyApiKeys.cs` prints one match. Strip BOM if needed. Build → 0 errors.

- [ ] **Step 5: Run the full unit + arch suites**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Api.Tests.Authorization|FullyQualifiedName~Dispatching|FullyQualifiedName~Crypto|FullyQualifiedName~Context"` → PASS.
Run: `dotnet test tests/DeveloperPlatform.ArchitectureTests` → PASS (10).
(The 5 `WebApplicationFactory` tests in `Projects`/`Auth` need RabbitMQ; the `ApiAuthorizationTests` 401 test, if it runs, needs the broker — unrelated to this change.)

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(authz): remove legacy ApiKey/ApiKeyScope in favor of credentials"
```

---

## Self-Review

**1. Spec coverage (Slice 4):** `ApiKeyCredential` (hash, prefix, expiry, revoke, last-used) — Task 1 ✅. Service-account creation with grants (SA-inherited scoping) — Task 2 ✅. Issue (plaintext once) / revoke / list — Task 2/4 ✅. API-key auth handler + machine principal → enforcement — Task 3 ✅. Endpoints — Task 4 ✅. Replace legacy `ApiKey`/`ApiKeyScope` + migration — Task 5 ✅. **Deferred (documented):** escalation guard (Slice 5), per-key downscoping, tenant-key provisioning (Slice 5).

**2. Placeholder scan:** No `TBD`/`TODO`. Every code step is complete; the two migrations are tool-generated with verification greps; Task 5's reference-removal steps enumerate every dangling site.

**3. Type consistency:** `ApiKeyCredential.Create(tenantId, serviceAccountId, name, keyPrefix, keyHash, expiresAt)` + `IsActive`/`Revoke`/`RecordUsage`; `IssueApiKeyResult(CredentialId, PlaintextKey, KeyPrefix)`; `CreateServiceAccountCommand(Name, Description, IReadOnlyList<GrantSpec>)`; `GrantSpec(Permission, ScopeType, Guid?)`; `ApiKeyAuthenticationHandler.{SchemeName, KeyPrefixMarker, ResolveCredentialAsync}`; the middleware reads `principal_id`/`principal_type` claims the handler emits — used identically across handlers, endpoints, auth, and tests.

**4. Risk notes:** Task 3 is the security-sensitive one — the credential lookup MUST use `IgnoreQueryFilters()` (the key determines the tenant; the request has no tenant context yet at auth time), and the tenant/principal come from the credential, never from caller-supplied claims. The policy scheme forwards by the `Bearer dpk_` prefix so JWTs are unaffected. Task 5 keeps the build green by removing only after the new stack is in place.
