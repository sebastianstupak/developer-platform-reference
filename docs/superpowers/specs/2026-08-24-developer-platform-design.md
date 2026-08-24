# Developer Platform Reference — Design Spec
_2026-08-24_

## Overview

A multi-tenant developer platform API demonstrating CQRS, outbox-based audit logging with crypto shredding, and flexible tenancy isolation. Modelled after the concerns of a real developer platform (API key management, environments, secrets) with production-grade patterns.

---

## Domain Features

| Feature | Description |
|---|---|
| Tenants | Root aggregate. Owns encryption key for crypto shredding. |
| Projects | Belong to a tenant. Logical grouping of environments and keys. |
| Environments | dev / staging / prod per project. |
| ApiKeys | Scoped credentials. Belong to a project or environment. |
| Secrets | Encrypted-at-rest env vars / credentials. Belong to an environment. |
| AuditLog | Every command is audited via outbox → RabbitMQ → audit store. |

---

## Architecture & Project Structure

```
src/
  DeveloperPlatform.Api/              HTTP layer — endpoints, middleware, DI wiring
  DeveloperPlatform.Application/      CQRS — dispatcher, commands, queries, handlers, interfaces
  DeveloperPlatform.Infrastructure/   EF Core, MariaDB, RabbitMQ, outbox relay, crypto
  DeveloperPlatform.Domain/           Entities, domain rules — zero outward dependencies

tests/
  DeveloperPlatform.Api.Tests/        Integration tests
  DeveloperPlatform.ArchitectureTests/ Layer dependency rules via NetArchTest
```

**Dependency rule:**
```
Api       → Application → Domain
Infrastructure → Application, Domain
Api       → Infrastructure (DI wiring only)
```

Domain has zero outward dependencies. Application has no knowledge of EF Core or RabbitMQ — only interfaces it defines. Infrastructure implements those interfaces.

---

## Section 1 — CQRS Dispatcher & Execution Context

### Interfaces

```csharp
// Write side
interface ICommand<TResult> { }
interface ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct);
}

// Read side — no side effects
interface IQuery<TResult> { }
interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct);
}

// Entry points used by endpoints
interface ICommandDispatcher
{
    Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken ct)
        where TCommand : ICommand<TResult>;
}

interface IQueryDispatcher
{
    Task<TResult> SendAsync<TQuery, TResult>(TQuery query, CancellationToken ct)
        where TQuery : IQuery<TResult>;
}
```

### Dispatcher Execution Flow (CommandDispatcher)

1. Resolve `ICommandHandler<TCommand, TResult>` from `IServiceProvider`
2. Read attributes on `TCommand`:
   - `[CrossTenant]` → validate `Reason` non-empty; in Mode C throw `NotSupportedException`; set `IExecutionContext.IsCrossTenantOperation = true`
   - `[SkipAudit]` → skip outbox write
3. Open DB transaction
4. Call `handler.HandleAsync(command, ct)`
5. Unless `[SkipAudit]`: scrub `[SensitiveData]` properties → serialize → encrypt with tenant key → write `AuditOutboxEntry` in same transaction
6. Commit transaction
7. On exception: write a separate `AuditOutboxEntry` with status = Failed (scrubbed payload, no sensitive values) if not `[SkipAudit]`, then re-throw. Failed entries go through the same relay → RabbitMQ → consumer path as successful ones.

QueryDispatcher is simpler — resolves handler, calls it, no transaction, no audit.

### Execution Context

Populated by middleware from JWT claims or resolved API key. Injected into DbContext for global query filters.

```csharp
interface IExecutionContext
{
    Guid TenantId { get; }
    Guid? UserId { get; }           // null when authenticated via API key
    Guid? ApiKeyId { get; }         // null when authenticated via user session
    Guid? ProjectId { get; }        // null for tenant-level operations
    Guid? EnvironmentId { get; }    // null for project-level operations
    string IpAddress { get; }
    bool IsCrossTenantOperation { get; set; }  // set by dispatcher, read by EF filter
}
```

### Command Attributes

```csharp
// Opt-out of audit — for high-frequency noise commands only
[SkipAudit]

// Mark a command property as sensitive — value replaced with "[REDACTED]" before outbox serialization
[SensitiveData]  // applied to properties

// Explicitly declare cross-tenant intent — Reason is mandatory and non-empty
[CrossTenant(Reason = "...")]  // applied to command class
```

`[CrossTenant]` without a `Reason` string throws at dispatch time. This is enforced in the dispatcher, not at compile time, but the attribute constructor requires the string parameter — empty string throws.

---

## Section 2 — Multi-Tenancy

### Deployment Modes

```csharp
enum TenancyMode { SharedTables, DatabasePerTenant }
```

Configured at startup via `appsettings.json`. Same application code, different infrastructure wiring.

### Mode A — Shared Tables

All tenant data in the same schema. TenantId is enforced structurally via EF Core global query filters — not by convention in individual handlers.

**Entity marker interface:**

```csharp
interface ITenantScoped
{
    Guid TenantId { get; }
}
```

**Automatic filter registration (Infrastructure):**

`OnModelCreating` scans all `ITenantScoped` entity types and applies `HasQueryFilter` pointing to `IExecutionContext.TenantId`. When `IsCrossTenantOperation` is true, the filter short-circuits.

**Architecture test enforces coverage:**

```csharp
// IEntity is the base marker interface for all domain entities (Id + CreatedAt).
// Any domain entity missing ITenantScoped fails CI.
Types.InAssembly(domainAssembly)
    .That().AreNotAbstract().And().ImplementInterface(typeof(IEntity))
    .Should().ImplementInterface(typeof(ITenantScoped))
```

### Mode C — Database Per Tenant

`ITenantConnectionResolver` maps `TenantId → connection string`. DbContext constructed per-request with resolved connection. No global query filter needed — isolation is physical. `[CrossTenant]` commands throw `NotSupportedException` at dispatch time in this mode.

```csharp
interface ITenantConnectionResolver
{
    string Resolve(Guid tenantId);
}
```

### RabbitMQ Multi-Tenancy

Single vhost, shared exchanges. `TenantId` carried as a first-class message header:

```
Exchange:    developer-platform.audit
Routing key: audit.{tenantId}
Headers:     x-tenant-id, x-correlation-id, x-command-type
```

---

## Section 3 — Audit Outbox & Crypto Shredding

### Data Model

**AuditOutboxEntries** (same DB as application data, written in command transaction):

| Column | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| TenantId | Guid | indexed |
| Payload | blob | AES-256-GCM encrypted JSON (sensitive fields pre-scrubbed to "[REDACTED]") |
| KeyId | Guid | which tenant key encrypted this |
| CreatedAt | DateTime | |
| ProcessedAt | DateTime? | null = unprocessed |
| RetryCount | int | max 5, then dead-lettered |

**AuditEvents** (written by RabbitMQ consumer, separate table or schema):

| Column | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| TenantId | Guid | indexed |
| OccurredAt | DateTime | |
| CommandType | varchar(200) | unencrypted — queryable |
| Status | enum | Success \| Failed |
| UserId | Guid? | unencrypted |
| ApiKeyId | Guid? | unencrypted |
| ProjectId | Guid? | unencrypted |
| EnvironmentId | Guid? | unencrypted |
| IpAddress | varchar(45) | unencrypted |
| IsCrossTenant | bool | |
| CrossTenantReason | varchar(500)? | |
| EncryptedPayload | blob | AES-256-GCM, encrypted with tenant key |
| KeyId | Guid | FK to TenantEncryptionKeys |

Structural columns (type, status, who, when) are unencrypted and queryable. Payload is encrypted. After crypto shredding, structural record survives — payload becomes unreadable.

### Crypto Shredding

**TenantEncryptionKeys:**

| Column | Type | Notes |
|---|---|---|
| Id | Guid | PK, referenced as KeyId |
| TenantId | Guid | indexed |
| EncryptedKey | blob | per-tenant AES-256 key, encrypted by master key (envelope encryption) |
| CreatedAt | DateTime | |
| ShreddedAt | DateTime? | set on tenant deletion |

Envelope encryption: per-tenant AES-256 key is encrypted by a master key from config (designed to swap for KMS later). To shred: zero `EncryptedKey` bytes, set `ShreddedAt`. Existing `AuditEvents.EncryptedPayload` rows remain — unreadable without the key. Key rotation is supported: new key version = new `TenantEncryptionKeys` row, old events still reference old `KeyId`.

### Sensitive Data Scrubbing

Before encryption, dispatcher reflects over command properties tagged `[SensitiveData]` and replaces values with `"[REDACTED]"` in the JSON payload. Scrubbing happens before encryption — the encrypted blob never contains raw secret values.

### Outbox Relay (Background Worker)

Hosted service polls every 5 seconds:

```
SELECT top 50 WHERE ProcessedAt IS NULL AND RetryCount < 5
→ publish to RabbitMQ with TenantId header + correlation ID
→ UPDATE ProcessedAt = now
on failure: RetryCount++, FailedAt = now
after RetryCount = 5: move to dead-letter exchange, alert via log
```

Soft concurrency control: `UPDATE WHERE ProcessedAt IS NULL` is atomic — safe for single relay instance. Scale-out would require a distributed lock or competing-consumer queue partitioning by TenantId.

---

## Architecture Tests (NetArchTest)

| Test | Rule |
|---|---|
| Domain independence | Domain assembly has no dependency on Application, Infrastructure, or Api |
| Application purity | Application assembly has no dependency on Infrastructure |
| Tenant coverage | All `IEntity` implementations also implement `ITenantScoped` |
| Handler naming | All `ICommandHandler` implementations end with `CommandHandler` |
| No direct DbContext in Api | Api assembly does not reference EF Core DbContext directly |

---

## Out of Scope (for this project)

- External KMS (master key in config is the placeholder)
- API key hashing / prefix schemes (separate concern)
- Rate limiting (Redis — separate feature)
- UI / dashboard
