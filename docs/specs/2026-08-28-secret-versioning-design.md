# Secret Versioning / History — Design

**Date:** 2026-08-28
**Status:** Approved (design)
**Feature:** Keep an append-only version history for each secret so users can see who changed a value and when, reveal a prior version, and roll back.

## 1. Goal

Today a `Secret` holds a single `EncryptedValue` + `KeyId` + `UpdatedAt`; every write overwrites the previous value with no trace of what it was. This feature adds a durable, append-only history of every value a secret has held, with three new capabilities:

- **List versions** — see every version of a secret (number, when, who), without decrypting.
- **Reveal a version** — decrypt and view the value of any prior version.
- **Roll back** — restore a prior version's value as a new current version.

It reuses the existing per-tenant AES-256-GCM crypto (retained keys already make old ciphertext decryptable), the audit trail, and the existing permission model. No new permissions.

## 2. Approved decisions

These forks were settled during brainstorming:

1. **Rollback semantics: roll-forward.** Rolling back to version _K_ decrypts _K_ and writes it as a **new** version _N+1_ under the current key. History stays strictly append-only; nothing is deleted or rewritten. (Preview the user approved: `v4 -> rollback to v1 -> current = v5, value of v1`.)
2. **Storage: denormalized current + append-only history.** `Secret` remains the fast "current value" row so reveal/list stay untouched on the hot path; a new `SecretVersion` table holds the full history. The current value is deliberately mirrored in both places — a documented trade-off that avoids rewriting reveal/list/rotate.
3. **Permissions: reuse existing.** List and reveal-version require `SecretsRead`; rollback requires `SecretsWrite`. No new permission enum values.
4. **Retention: keep everything.** No pruning or version cap (YAGNI). Noted as a future extension.

## 3. Data model

### 3.1 `Secret` (modified)

`src/DeveloperPlatform.Domain/Secrets/Secret.cs`

Add one field and replace the single `UpdateValue` mutator with two explicit methods. The existing `EncryptedValue`/`KeyId`/`UpdatedAt` stay as the mirror of the latest version.

```csharp
public int CurrentVersion { get; private set; }   // 1-based; latest version number

// Create(...) sets CurrentVersion = 1 (unchanged signature otherwise)

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
```

Why two methods, not one: `UpdateValue` is called today by both `SetSecretCommandHandler` and `RotateTenantKeyCommandHandler`. A version bump belongs to a genuine value change (set, rollback), **not** to key rotation, which re-encrypts the same value (§9). Set and rollback call `SetNewVersion`; rotation calls `ReEncryptCurrent`. The handler reads `secret.CurrentVersion` after `SetNewVersion` to number the new `SecretVersion` row.

### 3.2 `SecretVersion` (new)

`src/DeveloperPlatform.Domain/Secrets/SecretVersion.cs` — `TenantEntity` (so it inherits `Id`, `TenantId`, `CreatedAt`, and the tenant global query filter).

```csharp
public class SecretVersion : TenantEntity
{
    public Guid SecretId { get; private set; }
    public int VersionNumber { get; private set; }        // 1-based, monotonic per secret
    public byte[] EncryptedValue { get; private set; } = [];
    public Guid KeyId { get; private set; }               // key that encrypted THIS version
    public int? RolledBackFrom { get; private set; }      // set when this version was produced by a rollback

    // Who created this version — mirrors what the audit trail stores, so the
    // same actor-resolution can turn these into a display name.
    public Guid? CreatedByPrincipalId { get; private set; }
    public string? CreatedByPrincipalType { get; private set; }  // "Member" | "ServiceAccount"
    public Guid? CreatedByUserId { get; private set; }           // the human behind a Member

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
            RolledBackFrom = rolledBackFrom,
        };
}
```

`CreatedAt` (from `TenantEntity`) records when the version was written.

### 3.3 EF configuration

`src/DeveloperPlatform.Infrastructure/Persistence/Configurations/SecretVersionConfiguration.cs` (new); register `DbSet<SecretVersion> SecretVersions` on `ApplicationDbContext`.

```csharp
builder.HasKey(v => v.Id);
builder.Property(v => v.EncryptedValue).IsRequired();
builder.Property(v => v.CreatedByPrincipalType).HasMaxLength(40);
builder.HasIndex(v => new { v.SecretId, v.VersionNumber }).IsUnique();  // integrity + concurrency guard
builder.HasOne<Secret>()                                                // no navigation property needed
    .WithMany()
    .HasForeignKey(v => v.SecretId)
    .OnDelete(DeleteBehavior.Cascade);                                  // deleting a secret drops its history
```

The unique `(SecretId, VersionNumber)` index doubles as the concurrency guard: two racing writes that compute the same next number collide on insert; one succeeds, the other surfaces as the already-mapped **409 Conflict** (duplicate-key → `ConflictException`).

### 3.4 Migration + backfill

New EF migration `SecretVersioning` (`dotnet ef migrations add SecretVersioning --project src/DeveloperPlatform.Infrastructure --startup-project src/DeveloperPlatform.Api`):

1. `CREATE TABLE SecretVersions ...` (generated).
2. `ALTER TABLE Secrets ADD CurrentVersion int NOT NULL DEFAULT 1` (generated from the new property).
3. **Hand-added `migrationBuilder.Sql(...)` backfill** — one v1 row per existing secret, copying the current ciphertext verbatim (no re-encryption; same `KeyId`):

```sql
INSERT INTO SecretVersions
  (Id, TenantId, SecretId, VersionNumber, EncryptedValue, KeyId, CreatedAt,
   CreatedByPrincipalId, CreatedByPrincipalType, CreatedByUserId, RolledBackFrom)
SELECT UUID(), s.TenantId, s.Id, 1, s.EncryptedValue, s.KeyId, s.UpdatedAt,
       NULL, NULL, NULL, NULL
FROM Secrets s;
```

Existing secrets all become `CurrentVersion = 1` (the column default) with a matching v1 history row whose author is unknown (`NULL` → resolves to blank in the UI). The migration is additive and reversible (`Down` drops the table and column).

## 4. Application layer (ports + DTOs)

- **`Application/Secrets/SetSecret/`** — unchanged command; the handler now also appends a version (§5).
- **`Application/Secrets/ListSecretVersions/`** (new):
  - `record ListSecretVersionsQuery(Guid ProjectId, Guid EnvironmentId, string Name) : IQuery<IReadOnlyList<SecretVersionSummary>>, IResourceScoped` with `[RequiresPermission(Permission.SecretsRead)]`, `ResourceScope => Scope.Environment(EnvironmentId)`.
  - `record SecretVersionSummary(int VersionNumber, DateTime CreatedAt, string? Actor, bool IsCurrent, int? RolledBackFrom)`.
- **`Application/Secrets/RevealSecretVersion/`** (new):
  - `record RevealSecretVersionCommand(Guid ProjectId, Guid EnvironmentId, string Name, int VersionNumber) : ICommand<RevealSecretVersionResult>, IResourceScoped` with `[RequiresPermission(Permission.SecretsRead)]`, environment scope. A **command** (not query) so it is audited, matching the existing `RevealSecretCommand`.
  - `record RevealSecretVersionResult(string Name, int VersionNumber, [property: SensitiveData] string Value)`.
- **`Application/Secrets/RollbackSecret/`** (new):
  - `record RollbackSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name, int TargetVersion) : ICommand<Unit>, IResourceScoped` with `[RequiresPermission(Permission.SecretsWrite)]`, environment scope. Audited.

## 5. Infrastructure (handlers + repository)

### 5.1 Repository

`ISecretRepository` / `SecretRepository` gain version methods (queries that only read for lists follow the existing `ListSecretsQueryHandler` pattern and hit the `DbContext` directly):

```csharp
Task AddVersionAsync(SecretVersion version, CancellationToken ct = default);
Task<SecretVersion?> GetVersionAsync(Guid secretId, int versionNumber, CancellationToken ct = default);
```

### 5.2 `SetSecretCommandHandler` (modified)

After creating or updating the `Secret`, append a `SecretVersion` in the same unit of work (single `SaveChanges` → atomic):

```csharp
var (payload, keyId) = await crypto.EncryptAsync(ctx.TenantId, command.Value, ct);
var existing = await repository.GetAsync(command.EnvironmentId, command.Name, ct);
Secret secret;
if (existing is null)
{
    secret = Secret.Create(ctx.TenantId, command.ProjectId, command.EnvironmentId, command.Name, payload, keyId);
    await repository.AddAsync(secret, ct);           // CurrentVersion = 1
}
else
{
    secret = existing;
    secret.SetNewVersion(payload, keyId);            // CurrentVersion++
}

await repository.AddVersionAsync(SecretVersion.Create(
    ctx.TenantId, secret.Id, secret.CurrentVersion, payload, keyId,
    ctx.PrincipalId, ctx.PrincipalType?.ToString(), ctx.UserId), ct);
```

### 5.3 `RollbackSecretCommandHandler` (new)

```csharp
var secret = await repository.GetAsync(command.EnvironmentId, command.Name, ct)
    ?? throw new KeyNotFoundException($"Secret '{command.Name}' not found.");
var target = await repository.GetVersionAsync(secret.Id, command.TargetVersion, ct)
    ?? throw new KeyNotFoundException($"Version {command.TargetVersion} of '{command.Name}' not found.");

var plaintext = await crypto.DecryptAsync(ctx.TenantId, target.EncryptedValue, target.KeyId, ct);
var (payload, keyId) = await crypto.EncryptAsync(ctx.TenantId, plaintext, ct);   // fresh current key

secret.SetNewVersion(payload, keyId);                                            // CurrentVersion++
await repository.AddVersionAsync(SecretVersion.Create(
    ctx.TenantId, secret.Id, secret.CurrentVersion, payload, keyId,
    ctx.PrincipalId, ctx.PrincipalType?.ToString(), ctx.UserId,
    rolledBackFrom: command.TargetVersion), ct);
```

### 5.4 `RevealSecretVersionCommandHandler` (new)

Loads the secret, loads the requested version, decrypts with **that version's** `KeyId` (retained keys guarantee it decrypts even after rotation), returns `(Name, VersionNumber, Value)`.

### 5.5 `ListSecretVersionsQueryHandler` (new)

Queries `DbContext` directly (like `ListSecretsQueryHandler`): resolve the secret id from `(EnvironmentId, Name)`, project the versions ordered `VersionNumber DESC`, then resolve `CreatedBy*` into a display name using the **shared actor resolver** (§6). `IsCurrent = version.VersionNumber == secret.CurrentVersion`.

## 6. Shared actor resolution (targeted refactor)

`GetAuditEventsQueryHandler.ResolveActor(...)` (Member → user email, ServiceAccount → name, else principal id) is exactly what the version list needs. Extract it into a small shared helper so both handlers use one implementation rather than duplicating the logic:

- New `src/DeveloperPlatform.Infrastructure/Common/ActorResolver.cs` with the same static method and a helper to build the `users`/`serviceAccounts` lookup dictionaries from a set of rows.
- Update `GetAuditEventsQueryHandler` to call it (behavior-preserving; existing audit tests must stay green).

This is the only change outside the secrets feature, and it is directly motivated by the new list handler needing the same "who" resolution.

## 7. API endpoints

Added to the existing secrets group in `SecretsEndpoints.cs`
(`/api/v1/projects/{projectId:guid}/environments/{environmentId:guid}/secrets`):

| Method & path | Command/Query | Permission | Response |
| --- | --- | --- | --- |
| `GET  /{name}/versions` | `ListSecretVersionsQuery` | `SecretsRead` | `[{ versionNumber, createdAt, actor, isCurrent, rolledBackFrom }]` |
| `POST /{name}/versions/{version:int}/reveal` | `RevealSecretVersionCommand` | `SecretsRead` | `{ name, versionNumber, value }` |
| `POST /{name}/rollback` (body `{ version }`) | `RollbackSecretCommand` | `SecretsWrite` | `204 No Content` |

Response DTOs (`SecretVersionResponse`, `RevealVersionResponse`, `RollbackSecretRequest`) live beside the existing ones in `SecretsEndpoints.cs`. `404` for unknown secret/version; `403` enforced by the dispatch pipeline; `409` for the version-number race.

## 8. Web UI

`src/DeveloperPlatform.Web/Components/Pages/EnvironmentSecrets.razor` — add a **History** icon button (`Icons.Material.Outlined.History`) to each secret row's action column, opening a new `SecretHistoryDialog`.

`SecretHistoryDialog.razor` (new):
- Loads `GetSecretVersionsAsync(projectId, envId, name)` and renders a list, newest first: `v{n}` · relative time (`TimeFormat.Relative`) · actor · a **current** chip on the active version · a subtle "rolled back from v{k}" note where set.
- Each non-current row offers **Reveal** (calls `RevealSecretVersionAsync`, then reuses the existing `RevealSecretDialog` with the returned value) and **Roll back to this** (confirm via `DialogService.ShowMessageBoxAsync`, call `RollbackSecretAsync`, snackbar, refresh the secrets grid + history).
- The current version shows **Reveal** only (rolling back to current is a no-op and is hidden).

`DeveloperPlatformApiClient` gains three methods mirroring the endpoints:
`GetSecretVersionsAsync`, `RevealSecretVersionAsync`, `RollbackSecretAsync`, plus a `SecretVersionDto(int VersionNumber, DateTime CreatedAt, string? Actor, bool IsCurrent, int? RolledBackFrom)`.

Reuses existing components/helpers: `RevealSecretDialog`, `TimeFormat.Relative`, `EnvTypeChip`, snackbar conventions, and the monospace value styling.

## 9. Crypto & rotation interaction

- Each `SecretVersion` stores its own `KeyId`. Because rotated keys are **retained**, any version decrypts with its recorded key regardless of later rotations — so reveal-old-version and rollback work across rotations with no special handling.
- **Key rotation is unchanged in effect.** `RotateTenantKeyCommandHandler` re-encrypts current `Secret.EncryptedValue` rows under the new key as it does today, now via `Secret.ReEncryptCurrent` (which does not touch `CurrentVersion`); historical `SecretVersion` rows keep their original `KeyId` (still decryptable via retained keys). Rotation does **not** create new versions or change version numbers — it is a re-encryption of the current value, not a value change. This is why `SetNewVersion` and `ReEncryptCurrent` are separate methods (§3.1).

## 10. Audit interaction

- `SetSecretCommand` is already audited; behavior unchanged (it now also appends a version).
- `RollbackSecretCommand` and `RevealSecretVersionCommand` are new command types and are audited automatically by the dispatch pipeline; they will appear in the audit log's command-type filter. Secret values remain `[SensitiveData]` and are never persisted to audit payloads.

## 11. Testing

- **Domain:** `Create` sets `CurrentVersion = 1`; `UpdateValue` increments it; `SecretVersion.Create` records `RolledBackFrom`.
- **Handlers (Infrastructure/integration):**
  - Setting a new secret creates v1; a second set creates v2 and advances `Secret.CurrentVersion`.
  - `ListSecretVersions` returns versions newest-first with correct `IsCurrent` and resolved actor.
  - `RevealSecretVersion` returns the exact historical plaintext.
  - **Reveal an old version still succeeds after a key rotation** (the retained-key guarantee — the important regression gate).
  - Rollback to v_K_ produces a new version whose value equals v_K_'s, sets `RolledBackFrom = K`, and advances current.
- **API:** auth (403 without permission), 404 for unknown secret/version, happy-path shapes for all three endpoints.
- **Web:** one happy-path e2e — open history, reveal a prior version, roll back, confirm the current value changed.
- **Regression:** existing audit tests stay green after the `ResolveActor` extraction.

## 12. Out of scope (YAGNI)

- No retention limits, pruning, or per-version expiry.
- No diff/compare view between versions.
- No new permissions or per-version ACLs.
- No bulk/all-secrets history view; history is per-secret.
- Version numbers are integers per secret; no global ordering or timestamps-as-identity.
