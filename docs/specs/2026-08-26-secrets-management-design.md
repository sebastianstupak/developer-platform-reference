# Secrets Management (Phase 5) — Design

**Status:** Approved (design)
**Date:** 2026-08-26
**Depends on:** Phase 4 authorization (permissions, scoped enforcement, audit outbox, per-tenant crypto)

## Goal

Let a tenant store, read, and rotate encrypted secrets scoped to a project
environment, gated by the existing `secrets:read` / `secrets:write`
permissions and recorded through the existing audit outbox. This is the
feature the authorization and crypto layers were built to support:
`Domain/Secrets/Secret`, the `Secrets` table, per-tenant AES-256-GCM crypto
(`ITenantCryptoService` with key versioning), and the `secrets:*` permissions
already exist but have no application, API, or UI surface.

## Architecture

Mirror the established vertical-slice CQRS architecture exactly. Every
operation is a command or query dispatched through `CommandDispatcher` /
`QueryDispatcher`, so scope-aware authorization, auditing, and the
transaction boundary are inherited rather than re-implemented. No new
cross-cutting infrastructure. Secrets are addressed by nested REST routes
that carry the project and environment ids; commands/queries implement
`IResourceScoped` to have their permission checked at `Scope.Environment`.

Rejected alternatives: a standalone secrets service the endpoints call
directly (bypasses the dispatcher, losing automatic authz + audit); an
external secret store such as Vault (defeats the built-in per-tenant crypto
this reference exists to demonstrate).

## Global Constraints

- .NET 10, existing Clean Architecture layering (Domain / Application /
  Infrastructure / Api / Web). Follow existing slice conventions.
- All mutating operations go through `CommandDispatcher` (audited,
  transactional); reads through `QueryDispatcher`, except **RevealSecret**,
  which is a command specifically so the value access is audited.
- Plaintext secret values must never reach the audit log: the value-bearing
  property on `SetSecretCommand` is marked `[SensitiveData]` so the scrubber
  redacts it.
- Scope on secret operations is `Scope.Environment(environmentId)` derived
  from the route via `IResourceScoped`, not from JWT `environment_id` claims
  (those exist only on scoped machine tokens). The authorization service
  already lets a `Project`- or `Tenant`-scoped grant satisfy an
  `Environment`-scoped check (`Scope.Encompasses`).
- Repo commit conventions: no AI co-author trailers; pre-commit builds the
  whole solution; hooks must pass (never `--no-verify`).

## Slices

The work decomposes into four slices, each independently testable and
reviewable, implemented in order.

### Slice A — Environment management

Environments are the container secrets attach to and cannot be created
anywhere today. This slice adds full CRUD.

- **Domain:** `ProjectEnvironment` already exists (`ProjectId`, `Name`,
  `Type` in `{Development, Staging, Production}`). Add
  `IProjectEnvironmentRepository` + Infrastructure implementation. Keep the
  unique `(ProjectId, Name)` index (add it in the EF configuration).
- **Operations:**
  - `CreateEnvironmentCommand(Guid ProjectId, string Name, EnvironmentType Type)`
    → `CreateEnvironmentResult(Guid EnvironmentId)`, `[RequiresPermission(ProjectsWrite)]`,
    `IResourceScoped → Scope.Project(ProjectId)`.
  - `RenameEnvironmentCommand(Guid ProjectId, Guid EnvironmentId, string Name)`
    → `Unit`, `[RequiresPermission(ProjectsWrite)]`, `Scope.Project`.
  - `DeleteEnvironmentCommand(Guid ProjectId, Guid EnvironmentId)` → `Unit`,
    `[RequiresPermission(ProjectsWrite)]`, `Scope.Project`. **Cascade-deletes
    the environment's secrets** in the same transaction.
  - `GetEnvironmentsQuery(Guid ProjectId)` →
    `IReadOnlyList<EnvironmentSummary(Guid Id, string Name, EnvironmentType Type, DateTime CreatedAt)>`,
    `[RequiresPermission(ProjectsRead)]`, `Scope.Project`.
- **Domain method:** add `ProjectEnvironment.Rename(string name)` (guards
  non-empty).
- **Rationale:** environment lifecycle is project structure, so it is gated
  by `projects:write` / `projects:read`, not `secrets:*`.

### Slice B — Secrets CRUD

- **Domain:** `Secret` already exists (`ProjectId`, `EnvironmentId`, `Name`,
  `EncryptedValue`, `KeyId`, unique `(EnvironmentId, Name)`). **Add
  `UpdatedAt`** (`DateTime`, set on `Create` and `UpdateValue`) for the list
  view. Add `ISecretRepository` + implementation (lookup by
  `(EnvironmentId, Name)`, list by `EnvironmentId`, delete).
- **Operations:**
  - `SetSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name, [SensitiveData] string Value)`
    → `Unit`, `[RequiresPermission(SecretsWrite)]`,
    `IResourceScoped → Scope.Environment(EnvironmentId)`. **Upsert:** encrypt
    `Value` via `ITenantCryptoService.EncryptAsync` → `(payload, keyId)`; if a
    secret with that name exists in the environment, `UpdateValue(payload,
    keyId)`, else `Secret.Create(...)`.
  - `DeleteSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name)` →
    `Unit`, `[RequiresPermission(SecretsWrite)]`, `Scope.Environment`. 404 if
    absent.
  - `ListSecretsQuery(Guid ProjectId, Guid EnvironmentId)` →
    `IReadOnlyList<SecretSummary(string Name, DateTime CreatedAt, DateTime UpdatedAt)>`,
    `[RequiresPermission(SecretsRead)]`, `Scope.Environment`. **Never returns
    values.** Not audited.
  - `RevealSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name)` →
    `RevealSecretResult(string Name, string Value)`,
    `[RequiresPermission(SecretsRead)]`,
    `IResourceScoped → Scope.Environment`. A **command** (not a query) so the
    value access flows through the auditing dispatcher. The command has **no
    value field**, so the audit entry records who revealed which secret in
    which environment and when — never the plaintext. Decrypts with the
    secret's stored `KeyId` via `DecryptAsync`.
- **Value constraints:** UTF-8 text, max 64 KB (validate in the command
  handler; 400 on violation). Name max 200 (existing column), non-empty.

### Slice C — Key rotation

- **Operation:** `RotateTenantKeyCommand()` → `RotateTenantKeyResult(int
  SecretsReEncrypted)`, `[RequiresPermission(SecretsWrite)]`,
  `IResourceScoped → Scope.Tenant`.
- **Handler:** `CreateKeyAsync(tenantId)` adds a new key, then **persist it**
  (`SaveChangesAsync` within the dispatcher transaction) so it becomes the
  active key — `TenantCryptoService.GetActiveKeyAsync` selects the newest
  non-shredded key by `CreatedAt`, and it queries the database, so an unsaved
  add would be invisible to the subsequent `EncryptAsync`. Then enumerate
  every secret in the tenant (the tenant query filter already restricts to the
  caller's tenant); for each, `DecryptAsync(payload, secret.KeyId)` then
  `EncryptAsync(plaintext)` → `UpdateValue(newPayload, newKeyId)`. All inside
  the dispatcher's single transaction so a mid-rotation failure rolls back
  atomically (including the new key).
- **No key-store schema change.** The model is already "active key = newest
  non-shredded"; rotation just adds a newer key and re-encrypts secrets to it.
- **Old keys are retained, not shredded.** Audit outbox and `AuditEvent`
  payloads are also encrypted per `KeyId`; shredding an old key would render
  historical audit entries undecryptable (`DecryptAsync` throws on a shredded
  key). Rotation therefore never calls `ShredKeyAsync`, which stays reserved
  for full-tenant crypto-shredding (tenant deletion), out of scope here.
- **Rationale for the gate:** rotation is tenant-wide and high-impact, so it
  requires `secrets:write` at `Scope.Tenant` (Owner/Admin by default;
  environment-scoped Developers cannot rotate).

### Slice D — Web UI

- **Project detail page** (`/projects/{id}`): an environment tab strip
  (`MudTabs`). Each tab shows the environment's secrets in a `MudDataGrid`
  (columns: Name, Updated, actions) plus an **Add secret** button.
  - **Add / Edit secret** dialog: name + value field (`InputType.Password`,
    reveal toggle); submits `PUT .../secrets/{name}`.
  - **Reveal:** each row has a reveal action that calls the audited reveal
    endpoint, shows the value masked-by-default with a copy button (reuse the
    clipboard + snackbar pattern from `ManageKeysDialog`). Every reveal is a
    server round-trip (and an audit event).
  - **Delete secret:** confirm, then `DELETE`.
- **Environment management** on the same page: add environment (name + type),
  rename, delete (typed confirm warning that secrets are destroyed).
- **Rotate encryption key:** a control in a tenant/settings area, visible to
  Owner/Admin, with a typed confirm; calls `POST /api/v1/secrets/rotate-key`
  and reports the count re-encrypted.
- Follows the existing MudBlazor zinc theme and the globally-interactive
  render mode; new pages need no per-page `@rendermode`.
- **Web HTTP client:** extend `DeveloperPlatformApiClient` with the
  environment + secret calls and add DTOs to `Http/Models`.

## Data flow

1. **Set:** UI `PUT` → `SetSecretCommand` → dispatcher checks `secrets:write`
   @ env scope → handler encrypts (new `KeyId`) → upsert → audit entry
   (value `[REDACTED]`) → commit.
2. **List:** UI `GET` → `ListSecretsQuery` → checks `secrets:read` @ env scope
   → returns names + timestamps only.
3. **Reveal:** UI `POST .../reveal` → `RevealSecretCommand` → checks
   `secrets:read` @ env scope → decrypts with stored `KeyId` → returns value;
   audit entry records the access (no value).
4. **Rotate:** UI `POST /secrets/rotate-key` → `RotateTenantKeyCommand` →
   checks `secrets:write` @ tenant scope → new key + re-encrypt all secrets →
   audit entry → commit.

Machine access (CI): a service-account API key with `secrets:read` on the
target environment hits the same reveal route; the unified auth pipeline and
env-scoped grant evaluation apply unchanged.

## Error handling & edge cases

- Missing project / environment / secret → 404.
- Duplicate environment name in a project, or the upsert racing a create →
  409 (surfaced from the unique index).
- Value over 64 KB or empty name → 400.
- Insufficient permission or wrong scope → 403 (from the dispatcher).
- Deleting an environment removes its secrets in the same transaction; the UI
  requires a typed confirm.
- Rotation with zero secrets succeeds (creates the new key, re-encrypts none).
- Rotation failure mid-way rolls back atomically; the old active key stays
  active.

## Testing

- **Handler unit tests:** upsert creates then overwrites; reveal returns the
  original plaintext; list omits values; env-scoped Developer can read/write
  its environment but a tenant Viewer gets 403; delete-environment cascades to
  secrets; rotation re-encrypts every secret to the new key and a
  pre-rotation audit entry still decrypts with its retained key.
- **API auth tests:** a service-account API key scoped to the environment can
  reveal; one without `secrets:read` on that environment is forbidden.
- **Architecture tests** stay green (dependency direction, slice layout).
- New EF migration for `Secret.UpdatedAt` (+ any key active/inactive flag)
  applies cleanly.

## Out of scope (YAGNI)

Secret value history / rollback; binary secrets; `.env` import/export; secret
references or templating; per-secret ACLs; scheduled/automatic rotation;
hard key-shredding on rotation.
