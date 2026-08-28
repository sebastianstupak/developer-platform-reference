# Secret Versioning / History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every secret an append-only version history so users can list versions (who/when), reveal any prior version, and roll back to one as a new version.

**Architecture:** Keep `Secret` as the fast "current value" row (add a `CurrentVersion` counter) and add an append-only `SecretVersion` table holding every value the secret has held, each row carrying its own `KeyId` and the creator's identity. Set appends a version; rollback re-encrypts a prior version's plaintext under the current key as a new version. Reuse the existing per-tenant AES-256-GCM crypto (retained keys already decrypt old ciphertext) and the CQRS dispatch/audit pipeline.

**Tech Stack:** .NET 10, EF Core 9.x + Pomelo/MariaDB, custom CQRS (`ICommand`/`IQuery` + `ICommandDispatcher`/`IQueryDispatcher`), xUnit (InMemory `ApplicationDbContext` + `WebApplicationFactory<Program>`), Blazor Server + MudBlazor, Playwright e2e.

## Global Constraints

- Clean Architecture: dependencies point inward to `Domain`; `DeveloperPlatform.ArchitectureTests` enforces boundaries. Domain has no infra/EF references.
- CQRS handlers are registered **manually** in `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs` (no assembly scanning). Every new handler needs a registration line in the secrets block (currently lines ~114-119).
- The `CommandDispatcher`/`QueryDispatcher` owns the unit of work: it calls `SaveChanges` after the handler. Handlers add/mutate entities and return; they do **not** call `SaveChanges` themselves.
- Permissions: reuse `Permission.SecretsRead` (list + reveal-version) and `Permission.SecretsWrite` (rollback). Do **NOT** add new `Permission` enum values.
- Rollback is **roll-forward**: it writes a new version `N+1`; history is append-only; nothing is ever deleted or rewritten (except cascade on secret delete).
- Key rotation MUST NOT create versions or change version numbers. `RotateTenantKeyCommandHandler` re-encrypts the current value only, via `Secret.ReEncryptCurrent` (never `SetNewVersion`).
- Each `SecretVersion` stores its own `KeyId`; retained keys decrypt any version, so reveal-version and rollback work across rotations with no special handling.
- Secret values stay `[SensitiveData]` and are never persisted to audit payloads.
- Actor "who" resolution (Member → user email, ServiceAccount → name) is shared with the audit trail via one helper (`ActorResolver`); do not duplicate the logic.
- Commit hygiene: lefthook `pre-commit` runs arch-tests + full solution build + `dotnet format` (~1 min; allow up to 5 min for the commit command). The `commit-msg` hook **rejects AI co-author trailers** — never add `Co-Authored-By`/`Claude-Session` trailers. Never use `--no-verify`.
- Test conventions:
  - Handler/logic tests: InMemory `ApplicationDbContext` (`.UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`), `new TenantCryptoService(_db, Key)`, and a `TestExecutionContext : IExecutionContext`. See `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`.
  - Authorization tests: build a real `CommandDispatcher` with hand-wired DI. See `tests/DeveloperPlatform.Api.Tests/Secrets/SecretAuthorizationTests.cs`.
  - Endpoint 401 tests: `WebApplicationFactory<Program>` swapping the DbContext to InMemory and `services.RemoveAll<IHostedService>()`. See `tests/DeveloperPlatform.Api.Tests/Projects/ProjectsAuthorizationTests.cs`. **These boot the API host, which connects to RabbitMQ synchronously at startup, so `docker compose up -d db rabbitmq redis` must be running.**
- Migration generation uses the design-time factory `ApplicationDbContextFactory` (`ServerVersion.AutoDetect`), so `docker compose up -d db` must be running before `dotnet ef migrations add`.

---

## File Structure

**Domain** (`src/DeveloperPlatform.Domain/Secrets/`)
- `Secret.cs` (modify): add `CurrentVersion`; replace `UpdateValue` with `SetNewVersion` (bumps) + `ReEncryptCurrent` (no bump).
- `SecretVersion.cs` (create): append-only history entity.

**Infrastructure**
- `Persistence/ApplicationDbContext.cs` (modify): add `DbSet<SecretVersion> SecretVersions`.
- `Persistence/Configurations/SecretVersionConfiguration.cs` (create).
- `Migrations/<timestamp>_SecretVersioning.cs` (generated + hand-edited backfill).
- `Secrets/ISecretRepository.cs` + `Secrets/SecretRepository.cs` (modify): version methods.
- `Secrets/SetSecretCommandHandler.cs` (modify): append a version.
- `Secrets/DeleteSecretCommandHandler.cs` (modify): remove versions first.
- `Secrets/RotateTenantKeyCommandHandler.cs` (modify): `UpdateValue` → `ReEncryptCurrent`.
- `Secrets/ListSecretVersionsQueryHandler.cs` (create).
- `Secrets/RevealSecretVersionCommandHandler.cs` (create).
- `Secrets/RollbackSecretCommandHandler.cs` (create).
- `Common/ActorResolver.cs` (create): shared "who" resolution, extracted from the audit handler.
- `Audit/GetAuditEventsQueryHandler.cs` (modify): call `ActorResolver`.
- `ServiceCollectionExtensions.cs` (modify): register the three new handlers.

**Application** (`src/DeveloperPlatform.Application/Secrets/`)
- `ListSecretVersions/ListSecretVersionsQuery.cs` (create): query + `SecretVersionSummary`.
- `RevealSecretVersion/RevealSecretVersionCommand.cs` (create): command + result.
- `RollbackSecret/RollbackSecretCommand.cs` (create): command.

**Api** (`src/DeveloperPlatform.Api/Endpoints/Secrets/SecretsEndpoints.cs`, modify): three endpoints + response/request DTOs.

**Web**
- `Http/DeveloperPlatformApiClient.cs` (modify): three methods.
- `Http/Models/SecretDtos.cs` (modify): `SecretVersionDto`, `RevealVersionDto`.
- `Components/Pages/SecretHistoryDialog.razor` (create).
- `Components/Pages/EnvironmentSecrets.razor` (modify): History button + open handler.

**Tests** (`tests/DeveloperPlatform.Api.Tests/Secrets/`)
- Extend `SecretTests.cs` (domain + persistence + handler behavior).
- `SecretVersioningAuthorizationTests.cs` (create): rollback/reveal-version authz + reveal-after-rotation.
- `SecretVersionEndpointsTests.cs` (create): 401s.
- `tests/e2e/tests/secret-history.spec.ts` (create): happy-path e2e.

---

### Task 1: Domain — `SecretVersion` entity + `Secret` version methods

**Files:**
- Modify: `src/DeveloperPlatform.Domain/Secrets/Secret.cs`
- Create: `src/DeveloperPlatform.Domain/Secrets/SecretVersion.cs`
- Modify: `src/DeveloperPlatform.Infrastructure/Secrets/SetSecretCommandHandler.cs` (rename call only)
- Modify: `src/DeveloperPlatform.Infrastructure/Secrets/RotateTenantKeyCommandHandler.cs` (rename call only)
- Test: `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`

**Interfaces:**
- Produces:
  - `Secret.CurrentVersion` (`int`, 1-based), `Secret.SetNewVersion(byte[] encryptedValue, Guid keyId)` (advances `CurrentVersion`), `Secret.ReEncryptCurrent(byte[] encryptedValue, Guid keyId)` (leaves `CurrentVersion`).
  - `SecretVersion.Create(Guid tenantId, Guid secretId, int versionNumber, byte[] encryptedValue, Guid keyId, Guid? principalId, string? principalType, Guid? userId, int? rolledBackFrom = null)` returning a `SecretVersion` with public getters `SecretId, VersionNumber, EncryptedValue, KeyId, CreatedByPrincipalId, CreatedByPrincipalType, CreatedByUserId, RolledBackFrom` and inherited `Id, TenantId, CreatedAt`.

- [ ] **Step 1: Write the failing tests** — add to `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`. Also **replace** the existing `UpdateValue_Sets_UpdatedAt_Later` test (it calls the removed `UpdateValue`) with the `SetNewVersion` version below.

Replace the existing test:

```csharp
[Fact]
public void SetNewVersion_Advances_Version_And_Value()
{
    var s = Secret.Create(_tenant, _project, _env, "DB_URL", new byte[] { 1 }, Guid.NewGuid());
    Assert.Equal(1, s.CurrentVersion);
    var created = s.UpdatedAt;
    s.SetNewVersion(new byte[] { 2 }, Guid.NewGuid());
    Assert.Equal(2, s.CurrentVersion);
    Assert.True(s.UpdatedAt >= created);
    Assert.Equal(2, s.EncryptedValue[0]);
}

[Fact]
public void ReEncryptCurrent_Changes_Value_But_Not_Version()
{
    var s = Secret.Create(_tenant, _project, _env, "DB_URL", new byte[] { 1 }, Guid.NewGuid());
    s.ReEncryptCurrent(new byte[] { 9 }, Guid.NewGuid());
    Assert.Equal(1, s.CurrentVersion);
    Assert.Equal(9, s.EncryptedValue[0]);
}

[Fact]
public void SecretVersion_Create_Records_Fields()
{
    var secretId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    var v = SecretVersion.Create(_tenant, secretId, 3, new byte[] { 7 }, Guid.NewGuid(),
        principalId: Guid.NewGuid(), principalType: "Member", userId: userId, rolledBackFrom: 1);
    Assert.Equal(secretId, v.SecretId);
    Assert.Equal(3, v.VersionNumber);
    Assert.Equal(7, v.EncryptedValue[0]);
    Assert.Equal("Member", v.CreatedByPrincipalType);
    Assert.Equal(userId, v.CreatedByUserId);
    Assert.Equal(1, v.RolledBackFrom);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Secrets.SecretTests"`
Expected: FAIL — `Secret` has no `CurrentVersion`/`SetNewVersion`/`ReEncryptCurrent`; `SecretVersion` does not exist (compile errors).

- [ ] **Step 3: Modify `Secret.cs`** — add `CurrentVersion`, set it in `Create`, and replace `UpdateValue` with the two explicit methods.

```csharp
using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Secrets;

public class Secret : TenantEntity
{
    public Guid ProjectId { get; private set; }
    public Guid EnvironmentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public byte[] EncryptedValue { get; private set; } = [];
    public Guid KeyId { get; private set; }   // which TenantEncryptionKey encrypted the current value
    public int CurrentVersion { get; private set; }   // 1-based; number of the latest SecretVersion
    public DateTime UpdatedAt { get; private set; }

    private Secret() { }

    public static Secret Create(
        Guid tenantId, Guid projectId, Guid environmentId,
        string name, byte[] encryptedValue, Guid keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Secret
        {
            TenantId = tenantId,
            ProjectId = projectId,
            EnvironmentId = environmentId,
            Name = name,
            EncryptedValue = encryptedValue,
            KeyId = keyId,
            CurrentVersion = 1,
            UpdatedAt = DateTime.UtcNow
        };
    }

    // A new value: advances the version counter. Used by set and rollback.
    public void SetNewVersion(byte[] encryptedValue, Guid keyId)
    {
        EncryptedValue = encryptedValue;
        KeyId = keyId;
        CurrentVersion++;
        UpdatedAt = DateTime.UtcNow;
    }

    // Same value re-encrypted under a new key (key rotation): the version does NOT change.
    public void ReEncryptCurrent(byte[] encryptedValue, Guid keyId)
    {
        EncryptedValue = encryptedValue;
        KeyId = keyId;
        UpdatedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 4: Create `SecretVersion.cs`**

```csharp
using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Secrets;

// One immutable entry in a secret's append-only history. Each version keeps the
// key that encrypted it, so retained keys decrypt any version after rotation.
public class SecretVersion : TenantEntity
{
    public Guid SecretId { get; private set; }
    public int VersionNumber { get; private set; }       // 1-based, monotonic per secret
    public byte[] EncryptedValue { get; private set; } = [];
    public Guid KeyId { get; private set; }
    public int? RolledBackFrom { get; private set; }     // set when produced by a rollback

    // Who wrote this version (mirrors the audit trail's principal columns).
    public Guid? CreatedByPrincipalId { get; private set; }
    public string? CreatedByPrincipalType { get; private set; }  // "Member" | "ServiceAccount"
    public Guid? CreatedByUserId { get; private set; }           // the human behind a Member

    private SecretVersion() { }

    public static SecretVersion Create(
        Guid tenantId, Guid secretId, int versionNumber,
        byte[] encryptedValue, Guid keyId,
        Guid? principalId, string? principalType, Guid? userId,
        int? rolledBackFrom = null) => new()
        {
            TenantId = tenantId,
            SecretId = secretId,
            VersionNumber = versionNumber,
            EncryptedValue = encryptedValue,
            KeyId = keyId,
            CreatedByPrincipalId = principalId,
            CreatedByPrincipalType = principalType,
            CreatedByUserId = userId,
            RolledBackFrom = rolledBackFrom
        };
}
```

- [ ] **Step 5: Fix the two call sites so the solution compiles** — these still call the removed `UpdateValue`.

In `src/DeveloperPlatform.Infrastructure/Secrets/RotateTenantKeyCommandHandler.cs`, change the loop body call `secret.UpdateValue(payload, keyId);` to:

```csharp
            secret.ReEncryptCurrent(payload, keyId);
```

In `src/DeveloperPlatform.Infrastructure/Secrets/SetSecretCommandHandler.cs`, change `existing.UpdateValue(payload, keyId);` to:

```csharp
            existing.SetNewVersion(payload, keyId);
```

(The version-append logic is added in Task 3; this step only keeps the build green.)

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Secrets.SecretTests"`
Expected: PASS (all `SecretTests`, including the unchanged `Set_Then_Set_Overwrites_And_Encrypts`, `Reveal_Returns_Original_Plaintext`, etc.).

- [ ] **Step 7: Commit**

```bash
git add src/DeveloperPlatform.Domain/Secrets/Secret.cs src/DeveloperPlatform.Domain/Secrets/SecretVersion.cs src/DeveloperPlatform.Infrastructure/Secrets/RotateTenantKeyCommandHandler.cs src/DeveloperPlatform.Infrastructure/Secrets/SetSecretCommandHandler.cs tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs
git commit -m "feat(secrets): add SecretVersion entity and Secret version counter"
```

---

### Task 2: Persistence — EF mapping + migration with v1 backfill

**Files:**
- Modify: `src/DeveloperPlatform.Infrastructure/Persistence/ApplicationDbContext.cs:25`
- Create: `src/DeveloperPlatform.Infrastructure/Persistence/Configurations/SecretVersionConfiguration.cs`
- Create: `src/DeveloperPlatform.Infrastructure/Migrations/<timestamp>_SecretVersioning.cs` (generated)
- Test: `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`

**Interfaces:**
- Consumes: `SecretVersion` (Task 1).
- Produces: `ApplicationDbContext.SecretVersions` (`DbSet<SecretVersion>`); a `SecretVersions` table with unique `(SecretId, VersionNumber)` and cascade FK to `Secrets`; a `Secrets.CurrentVersion` column defaulting to 1 for existing rows.

- [ ] **Step 1: Write the failing persistence test** — add to `SecretTests.cs`:

```csharp
[Fact]
public async Task SecretVersions_Persist_And_Query_By_Secret_Newest_First()
{
    var secretId = Guid.NewGuid();
    _db.Add(SecretVersion.Create(_tenant, secretId, 1, new byte[] { 1 }, Guid.NewGuid(), null, null, null));
    _db.Add(SecretVersion.Create(_tenant, secretId, 2, new byte[] { 2 }, Guid.NewGuid(), null, null, null));
    await _db.SaveChangesAsync();

    var rows = await _db.SecretVersions.AsNoTracking()
        .Where(v => v.SecretId == secretId)
        .OrderByDescending(v => v.VersionNumber)
        .ToListAsync();

    Assert.Equal(new[] { 2, 1 }, rows.Select(v => v.VersionNumber));
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~SecretVersions_Persist_And_Query_By_Secret_Newest_First"`
Expected: FAIL — `ApplicationDbContext` has no `SecretVersions` member (compile error).

- [ ] **Step 3: Add the DbSet** — in `ApplicationDbContext.cs`, after line 25 (`public DbSet<Secret> Secrets => Set<Secret>();`):

```csharp
    public DbSet<SecretVersion> SecretVersions => Set<SecretVersion>();
```

- [ ] **Step 4: Create `SecretVersionConfiguration.cs`**

```csharp
using DeveloperPlatform.Domain.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class SecretVersionConfiguration : IEntityTypeConfiguration<SecretVersion>
{
    public void Configure(EntityTypeBuilder<SecretVersion> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.EncryptedValue).IsRequired();
        builder.Property(v => v.CreatedByPrincipalType).HasMaxLength(40);
        builder.HasIndex(v => new { v.SecretId, v.VersionNumber }).IsUnique();
        builder.HasOne<Secret>()
            .WithMany()
            .HasForeignKey(v => v.SecretId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 5: Verify the test passes** (InMemory uses the model, not the migration)

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~SecretVersions_Persist_And_Query_By_Secret_Newest_First"`
Expected: PASS.

- [ ] **Step 6: Generate the migration** (requires the DB up for `ServerVersion.AutoDetect`)

```bash
docker compose up -d db
dotnet ef migrations add SecretVersioning \
  --project src/DeveloperPlatform.Infrastructure \
  --startup-project src/DeveloperPlatform.Api
```

Expected: a new `Migrations/<timestamp>_SecretVersioning.cs` creating the `SecretVersions` table and adding the `CurrentVersion` column to `Secrets`.

- [ ] **Step 7: Hand-edit the generated migration's `Up()`** — two edits:

1. Find the `AddColumn<int>(name: "CurrentVersion", table: "Secrets", ... defaultValue: 0)` line and change `defaultValue: 0` to `defaultValue: 1` (existing secrets become version 1; the domain always sets an explicit value on insert, so the column default only affects pre-existing rows).

2. **After** the `CreateTable(name: "SecretVersions", ...)` block, append the backfill so every existing secret gets a v1 row copied verbatim from its current ciphertext (no re-encryption):

```csharp
            migrationBuilder.Sql(@"
INSERT INTO SecretVersions
  (Id, TenantId, SecretId, VersionNumber, EncryptedValue, KeyId, CreatedAt,
   CreatedByPrincipalId, CreatedByPrincipalType, CreatedByUserId, RolledBackFrom)
SELECT UUID(), s.TenantId, s.Id, 1, s.EncryptedValue, s.KeyId, s.UpdatedAt,
       NULL, NULL, NULL, NULL
FROM Secrets s;");
```

- [ ] **Step 8: Apply and verify the migration against MariaDB**

```bash
docker compose up -d db rabbitmq redis
dotnet ef database update \
  --project src/DeveloperPlatform.Infrastructure \
  --startup-project src/DeveloperPlatform.Api
```

Expected: applies cleanly. Then verify the backfill invariant (every secret has exactly one v1 and their counts line up):

```bash
docker compose exec -T db mariadb -uapp -papp developer_platform -e \
  "SELECT (SELECT COUNT(*) FROM Secrets) AS secrets, \
          (SELECT COUNT(*) FROM SecretVersions WHERE VersionNumber=1) AS v1_rows, \
          (SELECT COUNT(*) FROM Secrets WHERE CurrentVersion<>1) AS bad_current;"
```

Expected: `secrets == v1_rows` and `bad_current = 0`.

- [ ] **Step 9: Commit**

```bash
git add src/DeveloperPlatform.Infrastructure/Persistence/ApplicationDbContext.cs src/DeveloperPlatform.Infrastructure/Persistence/Configurations/SecretVersionConfiguration.cs src/DeveloperPlatform.Infrastructure/Migrations tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs
git commit -m "feat(secrets): map SecretVersion and add migration with v1 backfill"
```

---

### Task 3: Set appends a version + repository version methods

**Files:**
- Modify: `src/DeveloperPlatform.Infrastructure/Secrets/ISecretRepository.cs`
- Modify: `src/DeveloperPlatform.Infrastructure/Secrets/SecretRepository.cs`
- Modify: `src/DeveloperPlatform.Infrastructure/Secrets/SetSecretCommandHandler.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`

**Interfaces:**
- Consumes: `SecretVersion.Create` (Task 1), `ISecretRepository` (existing).
- Produces:
  - `ISecretRepository.AddVersionAsync(SecretVersion version, CancellationToken ct = default)` (`Task`).
  - `ISecretRepository.GetVersionAsync(Guid secretId, int versionNumber, CancellationToken ct = default)` (`Task<SecretVersion?>`).
  - `SetSecretCommandHandler` now writes a `SecretVersion` per set (create → v1; update → the new `CurrentVersion`).

- [ ] **Step 1: Write the failing tests** — add to `SecretTests.cs`:

```csharp
[Fact]
public async Task Set_New_Secret_Writes_Version_1()
{
    var crypto = new TenantCryptoService(_db, Key);
    await crypto.CreateKeyAsync(_tenant);
    await _db.SaveChangesAsync();
    var handler = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(
        new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db), crypto,
        new TestExecutionContext { TenantId = _tenant });

    await handler.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "API_KEY", "first"));
    await _db.SaveChangesAsync();

    var secret = await _db.Secrets.AsNoTracking().SingleAsync();
    var versions = await _db.SecretVersions.AsNoTracking().Where(v => v.SecretId == secret.Id).ToListAsync();
    Assert.Equal(1, secret.CurrentVersion);
    var v1 = Assert.Single(versions);
    Assert.Equal(1, v1.VersionNumber);
    Assert.Null(v1.RolledBackFrom);
    Assert.Equal("first", await crypto.DecryptAsync(_tenant, v1.EncryptedValue, v1.KeyId));
}

[Fact]
public async Task Set_Twice_Writes_Version_2_And_Advances_Current()
{
    var crypto = new TenantCryptoService(_db, Key);
    await crypto.CreateKeyAsync(_tenant);
    await _db.SaveChangesAsync();
    var handler = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(
        new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db), crypto,
        new TestExecutionContext { TenantId = _tenant });

    await handler.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "API_KEY", "first"));
    await _db.SaveChangesAsync();
    await handler.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "API_KEY", "second"));
    await _db.SaveChangesAsync();

    var secret = await _db.Secrets.AsNoTracking().SingleAsync();
    var versions = await _db.SecretVersions.AsNoTracking()
        .Where(v => v.SecretId == secret.Id).OrderBy(v => v.VersionNumber).ToListAsync();
    Assert.Equal(2, secret.CurrentVersion);
    Assert.Equal(new[] { 1, 2 }, versions.Select(v => v.VersionNumber));
    Assert.Equal("first", await crypto.DecryptAsync(_tenant, versions[0].EncryptedValue, versions[0].KeyId));
    Assert.Equal("second", await crypto.DecryptAsync(_tenant, versions[1].EncryptedValue, versions[1].KeyId));
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Set_New_Secret_Writes_Version_1|FullyQualifiedName~Set_Twice_Writes_Version_2_And_Advances_Current"`
Expected: FAIL — no `SecretVersions` rows written (Assert.Single / sequence mismatch).

- [ ] **Step 3: Add the repository methods** — in `ISecretRepository.cs`, add inside the interface:

```csharp
    Task AddVersionAsync(SecretVersion version, CancellationToken ct = default);
    Task<SecretVersion?> GetVersionAsync(Guid secretId, int versionNumber, CancellationToken ct = default);
```

In `SecretRepository.cs`, add the implementations (the file already has `using Microsoft.EntityFrameworkCore;`):

```csharp
    public async Task AddVersionAsync(SecretVersion version, CancellationToken ct = default)
        => await db.SecretVersions.AddAsync(version, ct);

    public async Task<SecretVersion?> GetVersionAsync(Guid secretId, int versionNumber, CancellationToken ct = default)
        => await db.SecretVersions.FirstOrDefaultAsync(v => v.SecretId == secretId && v.VersionNumber == versionNumber, ct);
```

- [ ] **Step 4: Append a version in `SetSecretCommandHandler.cs`** — replace the whole `HandleAsync` body:

```csharp
    public async Task<Unit> HandleAsync(SetSecretCommand command, CancellationToken ct = default)
    {
        if (Encoding.UTF8.GetByteCount(command.Value) > MaxValueBytes)
        {
            throw new ArgumentException($"Secret value exceeds {MaxValueBytes} bytes.");
        }

        var (payload, keyId) = await crypto.EncryptAsync(ctx.TenantId, command.Value, ct);
        var existing = await repository.GetAsync(command.EnvironmentId, command.Name, ct);
        Secret secret;
        if (existing is null)
        {
            secret = Secret.Create(ctx.TenantId, command.ProjectId, command.EnvironmentId, command.Name, payload, keyId);
            await repository.AddAsync(secret, ct);
        }
        else
        {
            secret = existing;
            secret.SetNewVersion(payload, keyId);
        }

        await repository.AddVersionAsync(SecretVersion.Create(
            ctx.TenantId, secret.Id, secret.CurrentVersion, payload, keyId,
            ctx.PrincipalId, ctx.PrincipalType?.ToString(), ctx.UserId), ct);

        return Unit.Value;
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Secrets.SecretTests"`
Expected: PASS (new tests plus the existing `Set_Then_Set_Overwrites_And_Encrypts`, which still sees one `Secrets` row).

- [ ] **Step 6: Commit**

```bash
git add src/DeveloperPlatform.Infrastructure/Secrets/ISecretRepository.cs src/DeveloperPlatform.Infrastructure/Secrets/SecretRepository.cs src/DeveloperPlatform.Infrastructure/Secrets/SetSecretCommandHandler.cs tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs
git commit -m "feat(secrets): append a version on every set"
```

---

### Task 4: Deleting a secret removes its versions

**Files:**
- Modify: `src/DeveloperPlatform.Infrastructure/Secrets/ISecretRepository.cs`
- Modify: `src/DeveloperPlatform.Infrastructure/Secrets/SecretRepository.cs`
- Modify: `src/DeveloperPlatform.Infrastructure/Secrets/DeleteSecretCommandHandler.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`

**Interfaces:**
- Produces: `ISecretRepository.RemoveVersionsForSecretAsync(Guid secretId, CancellationToken ct = default)` (`Task`).

*Note:* the DB has `ON DELETE CASCADE` (Task 2), but the InMemory provider does not enforce FK cascade, and cascade would not fire unless dependents are loaded. Deleting versions explicitly makes the behavior deterministic and testable on both providers.

- [ ] **Step 1: Write the failing test** — add to `SecretTests.cs`:

```csharp
[Fact]
public async Task Delete_Secret_Also_Removes_Its_Versions()
{
    var crypto = new TenantCryptoService(_db, Key);
    await crypto.CreateKeyAsync(_tenant);
    await _db.SaveChangesAsync();
    var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
    var setHandler = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(repo, crypto,
        new TestExecutionContext { TenantId = _tenant });
    await setHandler.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "X", "v1"));
    await _db.SaveChangesAsync();
    await setHandler.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "X", "v2"));
    await _db.SaveChangesAsync();

    var delHandler = new DeveloperPlatform.Infrastructure.Secrets.DeleteSecretCommandHandler(repo);
    await delHandler.HandleAsync(new DeveloperPlatform.Application.Secrets.DeleteSecret.DeleteSecretCommand(_project, _env, "X"));
    await _db.SaveChangesAsync();

    Assert.Empty(await _db.Secrets.ToListAsync());
    Assert.Empty(await _db.SecretVersions.ToListAsync());
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Delete_Secret_Also_Removes_Its_Versions"`
Expected: FAIL — versions remain (the `SecretVersions` list is not empty).

- [ ] **Step 3: Add the repository method** — in `ISecretRepository.cs`:

```csharp
    Task RemoveVersionsForSecretAsync(Guid secretId, CancellationToken ct = default);
```

In `SecretRepository.cs`:

```csharp
    public async Task RemoveVersionsForSecretAsync(Guid secretId, CancellationToken ct = default)
    {
        var versions = await db.SecretVersions.Where(v => v.SecretId == secretId).ToListAsync(ct);
        db.SecretVersions.RemoveRange(versions);
    }
```

- [ ] **Step 4: Remove versions in `DeleteSecretCommandHandler.cs`** — replace `HandleAsync`:

```csharp
    public async Task<Unit> HandleAsync(DeleteSecretCommand command, CancellationToken ct = default)
    {
        var secret = await repository.GetAsync(command.EnvironmentId, command.Name, ct)
            ?? throw new KeyNotFoundException($"Secret '{command.Name}' not found.");
        await repository.RemoveVersionsForSecretAsync(secret.Id, ct);
        repository.Delete(secret);
        return Unit.Value;
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Secrets.SecretTests"`
Expected: PASS (including the existing `Delete_Removes_Secret_And_404_When_Absent`).

- [ ] **Step 6: Commit**

```bash
git add src/DeveloperPlatform.Infrastructure/Secrets/ISecretRepository.cs src/DeveloperPlatform.Infrastructure/Secrets/SecretRepository.cs src/DeveloperPlatform.Infrastructure/Secrets/DeleteSecretCommandHandler.cs tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs
git commit -m "feat(secrets): cascade-delete versions with the secret"
```

---

### Task 5: List versions query + shared `ActorResolver`

**Files:**
- Create: `src/DeveloperPlatform.Infrastructure/Common/ActorResolver.cs`
- Modify: `src/DeveloperPlatform.Infrastructure/Audit/GetAuditEventsQueryHandler.cs`
- Create: `src/DeveloperPlatform.Application/Secrets/ListSecretVersions/ListSecretVersionsQuery.cs`
- Create: `src/DeveloperPlatform.Infrastructure/Secrets/ListSecretVersionsQueryHandler.cs`
- Modify: `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`

**Interfaces:**
- Produces:
  - `ActorResolver.Resolve(string? principalType, Guid? userId, Guid? principalId, IReadOnlyDictionary<Guid,string> users, IReadOnlyDictionary<Guid,string> serviceAccounts)` → `string?`.
  - `ListSecretVersionsQuery(Guid ProjectId, Guid EnvironmentId, string Name) : IQuery<IReadOnlyList<SecretVersionSummary>>, IResourceScoped` (`[RequiresPermission(Permission.SecretsRead)]`).
  - `SecretVersionSummary(int VersionNumber, DateTime CreatedAt, string? Actor, bool IsCurrent, int? RolledBackFrom)`.

- [ ] **Step 1: Write the failing test** — add to `SecretTests.cs` (uses `DeveloperPlatform.Domain.Identity`; add `using DeveloperPlatform.Domain.Identity;` at the top of the test file if not present):

```csharp
[Fact]
public async Task ListVersions_Returns_Newest_First_With_Current_And_Actor()
{
    var crypto = new TenantCryptoService(_db, Key);
    await crypto.CreateKeyAsync(_tenant);
    await _db.SaveChangesAsync();

    var user = User.Create("kc-sub-1", "alice@example.com", "Alice");
    _db.Users.Add(user);
    await _db.SaveChangesAsync();

    var ctx = new TestExecutionContext { TenantId = _tenant };
    var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);

    // Two versions; the second authored by a known Member user.
    var (p1, k1) = await crypto.EncryptAsync(_tenant, "one");
    var secret = Secret.Create(_tenant, _project, _env, "TOKEN", p1, k1);
    await repo.AddAsync(secret);
    await repo.AddVersionAsync(SecretVersion.Create(_tenant, secret.Id, 1, p1, k1, null, null, null));
    var (p2, k2) = await crypto.EncryptAsync(_tenant, "two");
    secret.SetNewVersion(p2, k2);
    await repo.AddVersionAsync(SecretVersion.Create(_tenant, secret.Id, 2, p2, k2,
        principalId: Guid.NewGuid(), principalType: "Member", userId: user.Id));
    await _db.SaveChangesAsync();

    var handler = new DeveloperPlatform.Infrastructure.Secrets.ListSecretVersionsQueryHandler(_db);
    var list = await handler.HandleAsync(
        new DeveloperPlatform.Application.Secrets.ListSecretVersions.ListSecretVersionsQuery(_project, _env, "TOKEN"));

    Assert.Equal(new[] { 2, 1 }, list.Select(v => v.VersionNumber));
    Assert.True(list[0].IsCurrent);
    Assert.False(list[1].IsCurrent);
    Assert.Equal("alice@example.com", list[0].Actor);
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~ListVersions_Returns_Newest_First_With_Current_And_Actor"`
Expected: FAIL — `ListSecretVersionsQuery`/`ListSecretVersionsQueryHandler` do not exist (compile error).

- [ ] **Step 3: Create `ActorResolver.cs`** (move the logic out of the audit handler):

```csharp
namespace DeveloperPlatform.Infrastructure.Common;

// Shared "who did this" resolution: a Member resolves to the user's email,
// a ServiceAccount to its name, otherwise the raw principal id.
public static class ActorResolver
{
    public static string? Resolve(
        string? principalType, Guid? userId, Guid? principalId,
        IReadOnlyDictionary<Guid, string> users, IReadOnlyDictionary<Guid, string> serviceAccounts)
    {
        if (principalType == "Member" && userId is { } uid && users.TryGetValue(uid, out var email))
        {
            return email;
        }

        if (principalType == "ServiceAccount" && principalId is { } pid && serviceAccounts.TryGetValue(pid, out var name))
        {
            return name;
        }

        return principalId?.ToString();
    }
}
```

- [ ] **Step 4: Point the audit handler at the shared resolver** — in `GetAuditEventsQueryHandler.cs`:
  1. Add `using DeveloperPlatform.Infrastructure.Common;` to the usings.
  2. Replace the call `ResolveActor(r.PrincipalType, r.UserId, r.PrincipalId, users, sas)` with `ActorResolver.Resolve(r.PrincipalType, r.UserId, r.PrincipalId, users, sas)`.
  3. Delete the now-unused `internal static string? ResolveActor(...)` method (lines ~85-100).

- [ ] **Step 5: Create the query** — `src/DeveloperPlatform.Application/Secrets/ListSecretVersions/ListSecretVersionsQuery.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.ListSecretVersions;

[RequiresPermission(Permission.SecretsRead)]
public record ListSecretVersionsQuery(Guid ProjectId, Guid EnvironmentId, string Name)
    : IQuery<IReadOnlyList<SecretVersionSummary>>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}

public record SecretVersionSummary(int VersionNumber, DateTime CreatedAt, string? Actor, bool IsCurrent, int? RolledBackFrom);
```

- [ ] **Step 6: Create the handler** — `src/DeveloperPlatform.Infrastructure/Secrets/ListSecretVersionsQueryHandler.cs`:

```csharp
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Application.Secrets.ListSecretVersions;
using DeveloperPlatform.Infrastructure.Common;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class ListSecretVersionsQueryHandler(ApplicationDbContext db)
    : IQueryHandler<ListSecretVersionsQuery, IReadOnlyList<SecretVersionSummary>>
{
    public async Task<IReadOnlyList<SecretVersionSummary>> HandleAsync(
        ListSecretVersionsQuery query, CancellationToken ct = default)
    {
        var secret = await db.Secrets.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EnvironmentId == query.EnvironmentId && s.Name == query.Name, ct)
            ?? throw new KeyNotFoundException($"Secret '{query.Name}' not found.");

        var rows = await db.SecretVersions.AsNoTracking()
            .Where(v => v.SecretId == secret.Id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new
            {
                v.VersionNumber,
                v.CreatedAt,
                v.CreatedByPrincipalType,
                v.CreatedByUserId,
                v.CreatedByPrincipalId,
                v.RolledBackFrom
            })
            .ToListAsync(ct);

        var userIds = rows.Where(r => r.CreatedByUserId is not null).Select(r => r.CreatedByUserId!.Value).Distinct().ToList();
        var principalIds = rows.Where(r => r.CreatedByPrincipalId is not null).Select(r => r.CreatedByPrincipalId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        var sas = await db.ServiceAccounts.AsNoTracking()
            .Where(s => principalIds.Contains(s.PrincipalId)).ToDictionaryAsync(s => s.PrincipalId, s => s.Name, ct);

        return rows.Select(r => new SecretVersionSummary(
            r.VersionNumber, r.CreatedAt,
            ActorResolver.Resolve(r.CreatedByPrincipalType, r.CreatedByUserId, r.CreatedByPrincipalId, users, sas),
            r.VersionNumber == secret.CurrentVersion,
            r.RolledBackFrom)).ToList();
    }
}
```

- [ ] **Step 7: Register the handler** — in `ServiceCollectionExtensions.cs`, add after the `RotateTenantKeyCommand` registration (~line 119):

```csharp
        services.AddScoped<IQueryHandler<ListSecretVersionsQuery, IReadOnlyList<SecretVersionSummary>>, ListSecretVersionsQueryHandler>();
```

Add the matching `using DeveloperPlatform.Application.Secrets.ListSecretVersions;` and `using DeveloperPlatform.Infrastructure.Secrets;` at the top if not already present (the secrets handlers are already imported).

- [ ] **Step 8: Run the tests to verify they pass** (including the audit regression)

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Secrets.SecretTests|FullyQualifiedName~Audit.AuditQueryTests"`
Expected: PASS — the new list test and all existing audit query tests (proves the `ActorResolver` extraction is behavior-preserving).

- [ ] **Step 9: Commit**

```bash
git add src/DeveloperPlatform.Infrastructure/Common/ActorResolver.cs src/DeveloperPlatform.Infrastructure/Audit/GetAuditEventsQueryHandler.cs src/DeveloperPlatform.Application/Secrets/ListSecretVersions src/DeveloperPlatform.Infrastructure/Secrets/ListSecretVersionsQueryHandler.cs src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs
git commit -m "feat(secrets): list secret versions with shared actor resolver"
```

---

### Task 6: Reveal a specific version

**Files:**
- Create: `src/DeveloperPlatform.Application/Secrets/RevealSecretVersion/RevealSecretVersionCommand.cs`
- Create: `src/DeveloperPlatform.Infrastructure/Secrets/RevealSecretVersionCommandHandler.cs`
- Modify: `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`

**Interfaces:**
- Produces:
  - `RevealSecretVersionCommand(Guid ProjectId, Guid EnvironmentId, string Name, int VersionNumber) : ICommand<RevealSecretVersionResult>, IResourceScoped` (`[RequiresPermission(Permission.SecretsRead)]`).
  - `RevealSecretVersionResult(string Name, int VersionNumber, string Value)`.

- [ ] **Step 1: Write the failing tests** — add to `SecretTests.cs`. The second test is the retained-key regression gate (reveal an old version after a key rotation).

```csharp
[Fact]
public async Task RevealVersion_Returns_That_Versions_Plaintext()
{
    var crypto = new TenantCryptoService(_db, Key);
    await crypto.CreateKeyAsync(_tenant);
    await _db.SaveChangesAsync();
    var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
    var ctx = new TestExecutionContext { TenantId = _tenant };
    var set = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(repo, crypto, ctx);
    await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "K", "one"));
    await _db.SaveChangesAsync();
    await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "K", "two"));
    await _db.SaveChangesAsync();

    var handler = new DeveloperPlatform.Infrastructure.Secrets.RevealSecretVersionCommandHandler(repo, crypto, ctx);
    var v1 = await handler.HandleAsync(
        new DeveloperPlatform.Application.Secrets.RevealSecretVersion.RevealSecretVersionCommand(_project, _env, "K", 1));
    Assert.Equal(1, v1.VersionNumber);
    Assert.Equal("one", v1.Value);
}

[Fact]
public async Task RevealVersion_Still_Works_After_Key_Rotation()
{
    var crypto = new TenantCryptoService(_db, Key);
    await crypto.CreateKeyAsync(_tenant);
    await _db.SaveChangesAsync();
    var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
    var ctx = new TestExecutionContext { TenantId = _tenant };
    var set = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(repo, crypto, ctx);
    await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "K", "one"));
    await _db.SaveChangesAsync();

    // Rotate the tenant key: re-encrypts current values, retains the old key.
    var rotate = new DeveloperPlatform.Infrastructure.Secrets.RotateTenantKeyCommandHandler(_db, crypto, ctx);
    await rotate.HandleAsync(new DeveloperPlatform.Application.Secrets.RotateTenantKey.RotateTenantKeyCommand());
    await _db.SaveChangesAsync();

    var handler = new DeveloperPlatform.Infrastructure.Secrets.RevealSecretVersionCommandHandler(repo, crypto, ctx);
    var v1 = await handler.HandleAsync(
        new DeveloperPlatform.Application.Secrets.RevealSecretVersion.RevealSecretVersionCommand(_project, _env, "K", 1));
    Assert.Equal("one", v1.Value);

    // Rotation must not create a new version.
    var secret = await _db.Secrets.AsNoTracking().SingleAsync();
    Assert.Equal(1, secret.CurrentVersion);
    Assert.Single(await _db.SecretVersions.AsNoTracking().Where(v => v.SecretId == secret.Id).ToListAsync());
}

[Fact]
public async Task RevealVersion_Unknown_Version_Throws()
{
    var crypto = new TenantCryptoService(_db, Key);
    await crypto.CreateKeyAsync(_tenant);
    await _db.SaveChangesAsync();
    var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
    var ctx = new TestExecutionContext { TenantId = _tenant };
    var set = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(repo, crypto, ctx);
    await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "K", "one"));
    await _db.SaveChangesAsync();

    var handler = new DeveloperPlatform.Infrastructure.Secrets.RevealSecretVersionCommandHandler(repo, crypto, ctx);
    await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(
        new DeveloperPlatform.Application.Secrets.RevealSecretVersion.RevealSecretVersionCommand(_project, _env, "K", 99)));
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~RevealVersion"`
Expected: FAIL — `RevealSecretVersionCommand`/handler do not exist (compile error).

- [ ] **Step 3: Create the command** — `src/DeveloperPlatform.Application/Secrets/RevealSecretVersion/RevealSecretVersionCommand.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.RevealSecretVersion;

[RequiresPermission(Permission.SecretsRead)]
public record RevealSecretVersionCommand(Guid ProjectId, Guid EnvironmentId, string Name, int VersionNumber)
    : ICommand<RevealSecretVersionResult>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}

public record RevealSecretVersionResult(string Name, int VersionNumber, string Value);
```

- [ ] **Step 4: Create the handler** — `src/DeveloperPlatform.Infrastructure/Secrets/RevealSecretVersionCommandHandler.cs`:

```csharp
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.RevealSecretVersion;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class RevealSecretVersionCommandHandler(
    ISecretRepository repository, ITenantCryptoService crypto, IExecutionContext ctx)
    : ICommandHandler<RevealSecretVersionCommand, RevealSecretVersionResult>
{
    public async Task<RevealSecretVersionResult> HandleAsync(RevealSecretVersionCommand command, CancellationToken ct = default)
    {
        var secret = await repository.GetAsync(command.EnvironmentId, command.Name, ct)
            ?? throw new KeyNotFoundException($"Secret '{command.Name}' not found.");
        var version = await repository.GetVersionAsync(secret.Id, command.VersionNumber, ct)
            ?? throw new KeyNotFoundException($"Version {command.VersionNumber} of '{command.Name}' not found.");
        var value = await crypto.DecryptAsync(ctx.TenantId, version.EncryptedValue, version.KeyId, ct);
        return new RevealSecretVersionResult(secret.Name, version.VersionNumber, value);
    }
}
```

- [ ] **Step 5: Register the handler** — in `ServiceCollectionExtensions.cs`, after the `ListSecretVersionsQuery` registration:

```csharp
        services.AddScoped<ICommandHandler<RevealSecretVersionCommand, RevealSecretVersionResult>, RevealSecretVersionCommandHandler>();
```

Add `using DeveloperPlatform.Application.Secrets.RevealSecretVersion;` at the top if not present.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~RevealVersion"`
Expected: PASS (all three).

- [ ] **Step 7: Commit**

```bash
git add src/DeveloperPlatform.Application/Secrets/RevealSecretVersion src/DeveloperPlatform.Infrastructure/Secrets/RevealSecretVersionCommandHandler.cs src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs
git commit -m "feat(secrets): reveal a specific secret version"
```

---

### Task 7: Roll back to a version (roll-forward)

**Files:**
- Create: `src/DeveloperPlatform.Application/Secrets/RollbackSecret/RollbackSecretCommand.cs`
- Create: `src/DeveloperPlatform.Infrastructure/Secrets/RollbackSecretCommandHandler.cs`
- Modify: `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`

**Interfaces:**
- Produces: `RollbackSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name, int TargetVersion) : ICommand<Unit>, IResourceScoped` (`[RequiresPermission(Permission.SecretsWrite)]`).

- [ ] **Step 1: Write the failing test** — add to `SecretTests.cs`:

```csharp
[Fact]
public async Task Rollback_Creates_New_Version_With_Target_Value()
{
    var crypto = new TenantCryptoService(_db, Key);
    await crypto.CreateKeyAsync(_tenant);
    await _db.SaveChangesAsync();
    var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
    var ctx = new TestExecutionContext { TenantId = _tenant };
    var set = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(repo, crypto, ctx);
    await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "K", "one"));   // v1
    await _db.SaveChangesAsync();
    await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "K", "two"));   // v2
    await _db.SaveChangesAsync();

    var rollback = new DeveloperPlatform.Infrastructure.Secrets.RollbackSecretCommandHandler(repo, crypto, ctx);
    await rollback.HandleAsync(new DeveloperPlatform.Application.Secrets.RollbackSecret.RollbackSecretCommand(_project, _env, "K", 1));
    await _db.SaveChangesAsync();

    var secret = await _db.Secrets.AsNoTracking().SingleAsync();
    Assert.Equal(3, secret.CurrentVersion);
    var v3 = await _db.SecretVersions.AsNoTracking().SingleAsync(v => v.SecretId == secret.Id && v.VersionNumber == 3);
    Assert.Equal(1, v3.RolledBackFrom);
    Assert.Equal("one", await crypto.DecryptAsync(_tenant, v3.EncryptedValue, v3.KeyId));

    // The current pointer now decrypts to the rolled-back value.
    var reveal = new DeveloperPlatform.Infrastructure.Secrets.RevealSecretCommandHandler(repo, crypto, ctx);
    var current = await reveal.HandleAsync(new DeveloperPlatform.Application.Secrets.RevealSecret.RevealSecretCommand(_project, _env, "K"));
    Assert.Equal("one", current.Value);
}

[Fact]
public async Task Rollback_Unknown_Version_Throws()
{
    var crypto = new TenantCryptoService(_db, Key);
    await crypto.CreateKeyAsync(_tenant);
    await _db.SaveChangesAsync();
    var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
    var ctx = new TestExecutionContext { TenantId = _tenant };
    var set = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(repo, crypto, ctx);
    await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "K", "one"));
    await _db.SaveChangesAsync();

    var rollback = new DeveloperPlatform.Infrastructure.Secrets.RollbackSecretCommandHandler(repo, crypto, ctx);
    await Assert.ThrowsAsync<KeyNotFoundException>(() => rollback.HandleAsync(
        new DeveloperPlatform.Application.Secrets.RollbackSecret.RollbackSecretCommand(_project, _env, "K", 99)));
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Rollback"`
Expected: FAIL — `RollbackSecretCommand`/handler do not exist (compile error).

- [ ] **Step 3: Create the command** — `src/DeveloperPlatform.Application/Secrets/RollbackSecret/RollbackSecretCommand.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.RollbackSecret;

[RequiresPermission(Permission.SecretsWrite)]
public record RollbackSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name, int TargetVersion)
    : ICommand<Unit>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}
```

- [ ] **Step 4: Create the handler** — `src/DeveloperPlatform.Infrastructure/Secrets/RollbackSecretCommandHandler.cs`:

```csharp
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.RollbackSecret;
using DeveloperPlatform.Domain.Secrets;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class RollbackSecretCommandHandler(
    ISecretRepository repository, ITenantCryptoService crypto, IExecutionContext ctx)
    : ICommandHandler<RollbackSecretCommand, Unit>
{
    public async Task<Unit> HandleAsync(RollbackSecretCommand command, CancellationToken ct = default)
    {
        var secret = await repository.GetAsync(command.EnvironmentId, command.Name, ct)
            ?? throw new KeyNotFoundException($"Secret '{command.Name}' not found.");
        var target = await repository.GetVersionAsync(secret.Id, command.TargetVersion, ct)
            ?? throw new KeyNotFoundException($"Version {command.TargetVersion} of '{command.Name}' not found.");

        var plaintext = await crypto.DecryptAsync(ctx.TenantId, target.EncryptedValue, target.KeyId, ct);
        var (payload, keyId) = await crypto.EncryptAsync(ctx.TenantId, plaintext, ct);   // fresh current key

        secret.SetNewVersion(payload, keyId);   // advances CurrentVersion
        await repository.AddVersionAsync(SecretVersion.Create(
            ctx.TenantId, secret.Id, secret.CurrentVersion, payload, keyId,
            ctx.PrincipalId, ctx.PrincipalType?.ToString(), ctx.UserId,
            rolledBackFrom: command.TargetVersion), ct);

        return Unit.Value;
    }
}
```

- [ ] **Step 5: Register the handler** — in `ServiceCollectionExtensions.cs`, after the `RevealSecretVersionCommand` registration:

```csharp
        services.AddScoped<ICommandHandler<RollbackSecretCommand, Unit>, RollbackSecretCommandHandler>();
```

Add `using DeveloperPlatform.Application.Secrets.RollbackSecret;` at the top if not present.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Rollback"`
Expected: PASS (both).

- [ ] **Step 7: Commit**

```bash
git add src/DeveloperPlatform.Application/Secrets/RollbackSecret src/DeveloperPlatform.Infrastructure/Secrets/RollbackSecretCommandHandler.cs src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs
git commit -m "feat(secrets): roll back to a prior version as a new version"
```

---

### Task 8: API endpoints + authorization

**Files:**
- Modify: `src/DeveloperPlatform.Api/Endpoints/Secrets/SecretsEndpoints.cs`
- Create: `tests/DeveloperPlatform.Api.Tests/Secrets/SecretVersioningAuthorizationTests.cs`
- Create: `tests/DeveloperPlatform.Api.Tests/Secrets/SecretVersionEndpointsTests.cs`

**Interfaces:**
- Consumes: `ListSecretVersionsQuery`/`SecretVersionSummary` (Task 5), `RevealSecretVersionCommand`/`RevealSecretVersionResult` (Task 6), `RollbackSecretCommand` (Task 7).
- Produces routes under `/api/v1/projects/{projectId}/environments/{environmentId}/secrets`:
  - `GET  /{name}/versions`
  - `POST /{name}/versions/{version:int}/reveal`
  - `POST /{name}/rollback` (body `{ "version": <int> }`)

- [ ] **Step 1: Write the failing authorization tests** — `SecretVersioningAuthorizationTests.cs`. Mirror `SecretAuthorizationTests` but wire the new handlers into the dispatcher. This proves the permission gates via the real dispatch pipeline.

```csharp
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Application.Secrets.ListSecretVersions;
using DeveloperPlatform.Application.Secrets.RevealSecretVersion;
using DeveloperPlatform.Application.Secrets.RollbackSecret;
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

public class SecretVersioningAuthorizationTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private HttpExecutionContext _ctx = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _principal = Guid.NewGuid();
    private readonly Guid _project = Guid.NewGuid();
    private readonly Guid _env = Guid.NewGuid();
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

    private CommandDispatcher BuildCommands()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<SetSecretCommand, Unit>, SetSecretCommandHandler>();
        services.AddScoped<ICommandHandler<RollbackSecretCommand, Unit>, RollbackSecretCommandHandler>();
        services.AddScoped<ICommandHandler<RevealSecretVersionCommand, RevealSecretVersionResult>, RevealSecretVersionCommandHandler>();
        services.AddScoped<ISecretRepository, SecretRepository>();
        services.AddScoped(_ => _db);
        services.AddScoped<IExecutionContext>(_ => _ctx);
        services.AddScoped<ITenantCryptoService>(_ => new TenantCryptoService(_db, Key));
        var sp = services.BuildServiceProvider();
        var authz = new DeveloperPlatform.Infrastructure.Authorization.AuthorizationService(_db);
        return new CommandDispatcher(sp, _db, _ctx, new TenantCryptoService(_db, Key),
            new AuditOutboxRepository(_db), new SensitiveDataScrubber(), TenancyMode.SharedTables, authz);
    }

    private async Task GrantAsync(Permission permission)
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, permission, Scope.Environment(_env)));
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Rollback_Forbidden_Without_SecretsWrite()
    {
        await GrantAsync(Permission.SecretsWrite);
        await BuildCommands().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "one"));
        // Revoke by starting a fresh principal grant set: remove write grant.
        _db.PermissionGrants.RemoveRange(_db.PermissionGrants);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            BuildCommands().SendAsync<RollbackSecretCommand, Unit>(new RollbackSecretCommand(_project, _env, "K", 1)));
    }

    [Fact]
    public async Task Rollback_Allowed_With_SecretsWrite()
    {
        await GrantAsync(Permission.SecretsWrite);
        await BuildCommands().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "one"));
        await BuildCommands().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "two"));
        await BuildCommands().SendAsync<RollbackSecretCommand, Unit>(new RollbackSecretCommand(_project, _env, "K", 1));

        var secret = await _db.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal(3, secret.CurrentVersion);
    }

    [Fact]
    public async Task RevealVersion_Forbidden_Without_SecretsRead()
    {
        await GrantAsync(Permission.SecretsWrite);
        await BuildCommands().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "one"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            BuildCommands().SendAsync<RevealSecretVersionCommand, RevealSecretVersionResult>(
                new RevealSecretVersionCommand(_project, _env, "K", 1)));
    }
}
```

- [ ] **Step 2: Run them to verify they fail** (they compile-fail until the handlers from Tasks 6-7 exist — they do — but this task's point is the endpoints; run anyway to confirm the authz wiring)

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~SecretVersioningAuthorizationTests"`
Expected: PASS already (the handlers exist from Tasks 6-7). If any fail, fix before continuing. These guard the permission attributes.

- [ ] **Step 3: Add the endpoints** — in `SecretsEndpoints.cs`, add these `using`s at the top:

```csharp
using DeveloperPlatform.Application.Secrets.ListSecretVersions;
using DeveloperPlatform.Application.Secrets.RevealSecretVersion;
using DeveloperPlatform.Application.Secrets.RollbackSecret;
```

Inside `MapSecrets`, after the existing `DELETE /{name}` mapping (before the `admin` group), add:

```csharp
        group.MapGet("/{name}/versions", async (Guid projectId, Guid environmentId, string name, IQueryDispatcher d, CancellationToken ct) =>
        {
            var results = await d.SendAsync<ListSecretVersionsQuery, IReadOnlyList<SecretVersionSummary>>(
                new ListSecretVersionsQuery(projectId, environmentId, name), ct);
            return Results.Ok(results.Select(v => new SecretVersionResponse(v.VersionNumber, v.CreatedAt, v.Actor, v.IsCurrent, v.RolledBackFrom)));
        }).WithName("ListSecretVersions").Produces<IEnumerable<SecretVersionResponse>>();

        group.MapPost("/{name}/versions/{version:int}/reveal", async (Guid projectId, Guid environmentId, string name, int version, ICommandDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync<RevealSecretVersionCommand, RevealSecretVersionResult>(
                new RevealSecretVersionCommand(projectId, environmentId, name, version), ct);
            return Results.Ok(new RevealVersionResponse(result.Name, result.VersionNumber, result.Value));
        }).WithName("RevealSecretVersion").Produces<RevealVersionResponse>();

        group.MapPost("/{name}/rollback", async (Guid projectId, Guid environmentId, string name,
            [FromBody] RollbackSecretRequest req, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<RollbackSecretCommand, Unit>(new RollbackSecretCommand(projectId, environmentId, name, req.Version), ct);
            return Results.NoContent();
        }).WithName("RollbackSecret").Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
```

Add the DTOs beside the existing records at the bottom of the class:

```csharp
    public record SecretVersionResponse(int VersionNumber, DateTime CreatedAt, string? Actor, bool IsCurrent, int? RolledBackFrom);

    public record RevealVersionResponse(string Name, int VersionNumber, string Value);

    public record RollbackSecretRequest(int Version);
```

- [ ] **Step 4: Write the endpoint 401 tests** — `SecretVersionEndpointsTests.cs` (needs `docker compose up -d db rabbitmq redis`):

```csharp
using System.Net;
using System.Net.Http.Json;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DeveloperPlatform.Api.Tests.Secrets;

public sealed class SecretVersionEndpointsTests : IClassFixture<SecretVersionEndpointsTests.DevPlatformFactory>
{
    private readonly DevPlatformFactory _factory;
    public SecretVersionEndpointsTests(DevPlatformFactory factory) => _factory = factory;

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static string Base(Guid p, Guid e, string name) => $"/api/v1/projects/{p}/environments/{e}/secrets/{name}";

    [Fact]
    public async Task ListVersions_Returns_401_Without_Auth()
    {
        var r = await Client().GetAsync($"{Base(Guid.NewGuid(), Guid.NewGuid(), "K")}/versions");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task RevealVersion_Returns_401_Without_Auth()
    {
        var r = await Client().PostAsync($"{Base(Guid.NewGuid(), Guid.NewGuid(), "K")}/versions/1/reveal", null);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Rollback_Returns_401_Without_Auth()
    {
        var r = await Client().PostAsJsonAsync($"{Base(Guid.NewGuid(), Guid.NewGuid(), "K")}/rollback", new { version = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
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
                    opts.UseInMemoryDatabase("secret-version-endpoint-tests")
                        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                services.RemoveAll<IHostedService>();
            });
        }
    }
}
```

- [ ] **Step 5: Run all the task's tests to verify they pass** (services must be up)

```bash
docker compose up -d db rabbitmq redis
dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~SecretVersioningAuthorizationTests|FullyQualifiedName~SecretVersionEndpointsTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DeveloperPlatform.Api/Endpoints/Secrets/SecretsEndpoints.cs tests/DeveloperPlatform.Api.Tests/Secrets/SecretVersioningAuthorizationTests.cs tests/DeveloperPlatform.Api.Tests/Secrets/SecretVersionEndpointsTests.cs
git commit -m "feat(secrets): version history, reveal-version and rollback endpoints"
```

---

### Task 9: Web — history dialog with reveal + rollback

**Files:**
- Modify: `src/DeveloperPlatform.Web/Http/Models/SecretDtos.cs`
- Modify: `src/DeveloperPlatform.Web/Http/DeveloperPlatformApiClient.cs`
- Create: `src/DeveloperPlatform.Web/Components/Pages/SecretHistoryDialog.razor`
- Modify: `src/DeveloperPlatform.Web/Components/Pages/EnvironmentSecrets.razor`
- Test: `tests/e2e/tests/secret-history.spec.ts`

**Interfaces:**
- Consumes: the three endpoints (Task 8).
- Produces: `SecretVersionDto`, `RevealVersionDto`; `DeveloperPlatformApiClient.GetSecretVersionsAsync/RevealSecretVersionAsync/RollbackSecretAsync`; a `SecretHistoryDialog` component.

- [ ] **Step 1: Add the DTOs** — in `SecretDtos.cs`, append:

```csharp
public record SecretVersionDto(int VersionNumber, DateTime CreatedAt, string? Actor, bool IsCurrent, int? RolledBackFrom);

public record RevealVersionDto(string Name, int VersionNumber, string Value);
```

- [ ] **Step 2: Add the client methods** — in `DeveloperPlatformApiClient.cs`, after `DeleteSecretAsync` (before `RotateKeyAsync`):

```csharp
    public Task<IReadOnlyList<SecretVersionDto>> GetSecretVersionsAsync(
        Guid projectId, Guid environmentId, string name, CancellationToken ct = default)
        => GetListAsync<SecretVersionDto>(
            $"/api/v1/projects/{projectId}/environments/{environmentId}/secrets/{Uri.EscapeDataString(name)}/versions", ct);

    public async Task<string> RevealSecretVersionAsync(
        Guid projectId, Guid environmentId, string name, int version, CancellationToken ct = default)
    {
        var response = await _http.PostAsync(
            $"/api/v1/projects/{projectId}/environments/{environmentId}/secrets/{Uri.EscapeDataString(name)}/versions/{version}/reveal",
            null, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<RevealVersionDto>(ct);
        return body!.Value;
    }

    public async Task RollbackSecretAsync(
        Guid projectId, Guid environmentId, string name, int version, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/environments/{environmentId}/secrets/{Uri.EscapeDataString(name)}/rollback",
            new { version }, ct);
        response.EnsureSuccessStatusCode();
    }
```

- [ ] **Step 3: Create `SecretHistoryDialog.razor`**

```razor
@using DeveloperPlatform.Web.Components.Shared
@inject DeveloperPlatformApiClient ApiClient
@inject IDialogService DialogService
@inject ISnackbar Snackbar

<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6">History &mdash; @SecretName</MudText>
    </TitleContent>
    <DialogContent>
        <div style="width:min(560px, 92vw);">
            @if (_loading)
            {
                <MudProgressLinear Indeterminate="true" Color="Color.Primary" />
            }
            else if (_versions.Count == 0)
            {
                <MudText Typo="Typo.body2" Class="mud-text-secondary py-4">No history for this secret.</MudText>
            }
            else
            {
                <MudList T="SecretVersionDto" Dense="true">
                    @foreach (var v in _versions)
                    {
                        <MudListItem T="SecretVersionDto">
                            <MudStack Row="true" AlignItems="AlignItems.Center" Justify="Justify.SpaceBetween">
                                <div>
                                    <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
                                        <MudText Typo="Typo.subtitle2" Style="font-family:monospace;">v@(v.VersionNumber)</MudText>
                                        @if (v.IsCurrent)
                                        {
                                            <MudChip T="string" Size="Size.Small" Color="Color.Primary" Variant="Variant.Text" Class="ma-0">current</MudChip>
                                        }
                                        @if (v.RolledBackFrom is int from)
                                        {
                                            <MudText Typo="Typo.caption" Class="mud-text-secondary">rolled back from v@(from)</MudText>
                                        }
                                    </MudStack>
                                    <MudText Typo="Typo.caption" Class="mud-text-secondary">
                                        @TimeFormat.Relative(v.CreatedAt)@(string.IsNullOrEmpty(v.Actor) ? "" : $" · {v.Actor}")
                                    </MudText>
                                </div>
                                <MudStack Row="true" Spacing="1">
                                    <MudButton Size="Size.Small" Variant="Variant.Text"
                                               StartIcon="@Icons.Material.Outlined.Visibility"
                                               OnClick="@(() => RevealAsync(v))">Reveal</MudButton>
                                    @if (!v.IsCurrent)
                                    {
                                        <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Primary"
                                                   StartIcon="@Icons.Material.Outlined.Restore"
                                                   OnClick="@(() => RollbackAsync(v))">Roll back</MudButton>
                                    }
                                </MudStack>
                            </MudStack>
                        </MudListItem>
                    }
                </MudList>
            }
        </div>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Close" Color="Color.Default">Close</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = default!;

    [Parameter] public Guid ProjectId { get; set; }
    [Parameter] public Guid EnvironmentId { get; set; }
    [Parameter] public string SecretName { get; set; } = string.Empty;

    private IReadOnlyList<SecretVersionDto> _versions = [];
    private bool _loading = true;
    private bool _changed;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _versions = await ApiClient.GetSecretVersionsAsync(ProjectId, EnvironmentId, SecretName);
        _loading = false;
    }

    private async Task RevealAsync(SecretVersionDto v)
    {
        try
        {
            var value = await ApiClient.RevealSecretVersionAsync(ProjectId, EnvironmentId, SecretName, v.VersionNumber);
            var parameters = new DialogParameters<RevealSecretDialog>
            {
                { x => x.SecretName, $"{SecretName} (v{v.VersionNumber})" },
                { x => x.Value, value },
            };
            await DialogService.ShowAsync<RevealSecretDialog>($"Secret value — {SecretName} v{v.VersionNumber}", parameters);
        }
        catch
        {
            Snackbar.Add("Couldn't reveal this version.", Severity.Error);
        }
    }

    private async Task RollbackAsync(SecretVersionDto v)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Roll back secret",
            $"Restore v{v.VersionNumber} of \"{SecretName}\" as a new version?",
            yesText: "Roll back", cancelText: "Cancel");
        if (confirmed is not true)
        {
            return;
        }

        try
        {
            await ApiClient.RollbackSecretAsync(ProjectId, EnvironmentId, SecretName, v.VersionNumber);
            Snackbar.Add($"Rolled back to v{v.VersionNumber}.", Severity.Success);
            _changed = true;
            await LoadAsync();
        }
        catch
        {
            Snackbar.Add("Couldn't roll back.", Severity.Error);
        }
    }

    private void Close() => MudDialog.Close(DialogResult.Ok(_changed));
}
```

- [ ] **Step 4: Wire the History button** — in `EnvironmentSecrets.razor`, add a button to the action `CellTemplate` (the `TemplateColumn Title=""`), before the Reveal button:

```razor
                        <MudIconButton Icon="@Icons.Material.Outlined.History" Size="Size.Small" Color="Color.Default"
                                       aria-label="Version history" OnClick="@(() => OpenHistoryAsync(context.Item))" />
```

And add the handler to the `@code` block (next to `RevealSecretAsync`):

```csharp
    private async Task OpenHistoryAsync(SecretDto secret)
    {
        var parameters = new DialogParameters<SecretHistoryDialog>
        {
            { x => x.ProjectId, ProjectId },
            { x => x.EnvironmentId, EnvironmentId },
            { x => x.SecretName, secret.Name },
        };
        var dialog = await DialogService.ShowAsync<SecretHistoryDialog>($"History — {secret.Name}", parameters);
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: true })
        {
            await RefreshSecretsAsync();   // a rollback changed the current value
        }
    }
```

- [ ] **Step 5: Verify the Web build**

Run: `dotnet build src/DeveloperPlatform.Web -c Release`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Write the e2e happy-path test** — `tests/e2e/tests/secret-history.spec.ts`. Follow the existing spec structure in `tests/e2e/tests` (reuse the login/setup helpers already there). This test signs in, opens an environment, sets a secret twice, opens History, reveals v1, rolls back to v1, and asserts a new current version appears.

```typescript
import { test, expect } from "@playwright/test";
import { signIn } from "./helpers";   // reuse the repo's existing helper; adjust import to match

test("secret history: reveal a prior version and roll back", async ({ page }) => {
    await signIn(page);

    // Navigate to a project's environment secrets page (reuse existing navigation helpers/fixtures).
    // Create a secret and update it so there are two versions.
    const name = `E2E_HIST_${Date.now()}`;
    // ...use the existing "Add secret" / "Edit secret" dialog flows to set the value twice...

    // Open the history dialog for that secret.
    await page.getByRole("row", { name: new RegExp(name) }).getByRole("button", { name: "Version history" }).click();
    await expect(page.getByText("v2")).toBeVisible();
    await expect(page.getByText("current")).toBeVisible();

    // Roll back to v1.
    await page.getByRole("button", { name: "Roll back" }).first().click();
    await page.getByRole("button", { name: "Roll back" }).click();   // confirm dialog
    await expect(page.getByText("v3")).toBeVisible();
});
```

*Note for the implementer:* inspect the sibling specs in `tests/e2e/tests` first and reuse their sign-in/navigation helpers and selectors verbatim; the block above shows the assertions specific to this feature. If the sibling specs seed data via the API, do the same rather than driving every dialog by hand.

- [ ] **Step 7: Run the e2e test against the running stack**

```bash
# In one shell: bring up services + run API and Web per the repo's run recipe.
# Then:
cd tests/e2e && npx playwright test secret-history.spec.ts
```

Expected: PASS (1 test). If sign-in/navigation helpers differ, align the test with the sibling specs until green.

- [ ] **Step 8: Commit**

```bash
git add src/DeveloperPlatform.Web/Http/Models/SecretDtos.cs src/DeveloperPlatform.Web/Http/DeveloperPlatformApiClient.cs src/DeveloperPlatform.Web/Components/Pages/SecretHistoryDialog.razor src/DeveloperPlatform.Web/Components/Pages/EnvironmentSecrets.razor tests/e2e/tests/secret-history.spec.ts
git commit -m "feat(web): secret version history dialog with reveal and rollback"
```

---

## Final verification

- [ ] Run the whole test suite: `dotnet test developer-platform-reference.slnx -c Release` (services up). Expected: all green.
- [ ] Run format: `dotnet format developer-platform-reference.slnx --verify-no-changes`. Expected: no changes.
- [ ] Confirm CI parity: the migration applies from clean (`dotnet ef database update` on a fresh DB), tests pass, format passes — the same gates as `.github/workflows/ci.yml`.

---

## Self-Review (completed during authoring)

**Spec coverage:**
- §3.1 `Secret.CurrentVersion` → Task 1 (refined into `SetNewVersion`/`ReEncryptCurrent` to keep rotation from bumping versions, per §9).
- §3.2 `SecretVersion` → Task 1.
- §3.3 EF config → Task 2.
- §3.4 migration + v1 backfill → Task 2.
- §4/§5.2 set appends a version → Task 3.
- §5.1 repository version methods → Tasks 3-4.
- §4 list versions query + §5.5 handler → Task 5.
- §6 shared ActorResolver extraction → Task 5.
- §4/§5.4 reveal version → Task 6 (incl. §9 reveal-after-rotation gate).
- §4/§5.3 rollback → Task 7.
- §7 API endpoints → Task 8.
- §8 Web history UI → Task 9.
- §9 rotation unchanged / retained keys → enforced by `ReEncryptCurrent` (Task 1) and tested in Task 6.
- §10 audit of new commands → automatic (commands flow through the dispatcher); `RevealSecretVersionCommand`/`RollbackSecretCommand` are audited like `RevealSecretCommand`.
- §11 testing → Tasks 1-9 each carry their tests; retained-key gate in Task 6.
- §12 out of scope respected (no pruning, no new permissions, no diff view).

**Deviations from the written spec:** §3.1 sketched a single `UpdateValue` carrying the version increment. Because `RotateTenantKeyCommandHandler` also calls that method and §9 requires rotation NOT to change versions, the plan splits it into `SetNewVersion` (bumps) and `ReEncryptCurrent` (no bump). This is a faithful reconciliation of §3.1 with §9; the spec doc's §3.1 will be updated to match.

**Placeholder scan:** none — every code step contains full code; the only prose-guided step is the e2e test (Task 9 Step 6), which is deliberately aligned to the repo's existing e2e helpers that the implementer must read.

**Type consistency:** `SetNewVersion`/`ReEncryptCurrent`, `SecretVersion.Create(...)`, `AddVersionAsync`/`GetVersionAsync`/`RemoveVersionsForSecretAsync`, `ListSecretVersionsQuery`/`SecretVersionSummary`, `RevealSecretVersionCommand`/`RevealSecretVersionResult`, `RollbackSecretCommand`, and the API/Web DTOs are used consistently across tasks.
