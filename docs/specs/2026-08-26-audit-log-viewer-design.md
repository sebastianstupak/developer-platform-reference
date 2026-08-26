# Audit Log Viewer (Phase 6) — Design

**Status:** Approved (design)
**Date:** 2026-08-26
**Depends on:** Phase 4 authorization (`audit:read`, scoped enforcement, per-tenant crypto) and the existing RabbitMQ outbox → `AuditEvents` relay.

## Goal

Give an operator a browsable, filterable view of the tenant's audit trail —
who did what, when, with what outcome — gated by `audit:read`. Every mutating
command already lands in `AuditEvents` (via `CommandDispatcher` → outbox →
`AuditConsumer`), but nothing surfaces it. This adds the read side: a paged
query API and a Blazor page.

## Architecture

Same vertical-slice CQRS pattern as the rest of the codebase. Two queries and
one page:

- The **list** query reads only the denormalized `AuditEvents` columns (no
  decryption) so paging is cheap, and resolves actor GUIDs to display names
  with a LEFT JOIN.
- The **detail** query decrypts a single event's `EncryptedPayload` on demand
  via `ITenantCryptoService`, returning the scrubbed command JSON.

Offset paging (not keyset) — simpler and sufficient at this scale. Neither
query is itself audited: viewing the audit log is `audit:read`-gated and the
payload is already scrubbed, so auditing audit-reads would be pointless
regress.

## Global Constraints

- .NET 10, existing Clean Architecture layering; follow existing slice
  conventions (records, `[RequiresPermission]`, `IQueryDispatcher`,
  handlers query `ApplicationDbContext` directly for reads).
- Both queries carry `[RequiresPermission(Permission.AuditRead)]` at **Tenant
  scope** (no `IResourceScoped` → the dispatcher defaults to `Scope.Tenant`).
- `AuditEvent` is `ITenantScoped`, so the global query filter bounds every
  read to the caller's tenant automatically.
- The list must NOT decrypt payloads; the detail decrypts exactly one.
- Detail decryption must degrade gracefully: if the key is missing/shredded
  (`DecryptAsync` throws), return a "payload unavailable" marker, never a 500.
- Repo conventions: no AI co-author trailers; hooks must pass; `.cs` files are
  CRLF.

## Slices

### Slice A — Backend query API

**Data model:** no schema change. `AuditEvent` already has everything:
`OccurredAt`, `CommandType`, `Status` (`AuditStatus` = `Success` | `Failed`),
`PrincipalId`, `PrincipalType` (`"Member"` | `"ServiceAccount"`), `UserId`,
`ProjectId`, `EnvironmentId`, `IpAddress`, `IsCrossTenant`,
`CrossTenantReason`, `EncryptedPayload`, `KeyId`.

**Shared type:** add `PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)` in `DeveloperPlatform.Application/Common` (no existing paging type).

**List query**
- `GetAuditEventsQuery(AuditFilter Filter, int Page, int PageSize) : IQuery<PagedResult<AuditEventSummary>>`, `[RequiresPermission(Permission.AuditRead)]`.
- `AuditFilter(DateTime? From, DateTime? To, Guid? PrincipalId, string? CommandType, AuditStatus? Status, bool? CrossTenantOnly)`.
- `AuditEventSummary(Guid Id, DateTime OccurredAt, string CommandType, AuditStatus Status, string? ActorDisplay, string? PrincipalType, string IpAddress, bool IsCrossTenant, Guid? ProjectId, Guid? EnvironmentId)`.
- Handler: start from `db.AuditEvents.AsNoTracking()`; apply each non-null
  filter (`OccurredAt >= From`, `OccurredAt <= To`, `PrincipalId == …`,
  `CommandType == …`, `Status == …`, `IsCrossTenant` when `CrossTenantOnly ==
  true`); `OrderByDescending(OccurredAt)`; compute `Total` (count before
  paging); `Skip((Page-1)*PageSize).Take(PageSize)`. Resolve `ActorDisplay`
  with a LEFT JOIN: `Users.Email`/`DisplayName` on `UserId` for members,
  `ServiceAccounts.Name` on `PrincipalId` for service accounts; fall back to
  the raw `PrincipalId` when unresolved (deleted principal). `PageSize` is
  clamped to `[1, 100]`; default 25.

**Detail query**
- `GetAuditEventDetailQuery(Guid Id) : IQuery<AuditEventDetail>`, `[RequiresPermission(Permission.AuditRead)]`.
- `AuditEventDetail(AuditEventSummary Summary, string? CrossTenantReason, string PayloadJson, bool PayloadAvailable)`.
- Handler: load the event (tenant-filtered) — 404/`KeyNotFoundException` if
  absent; `DecryptAsync(tenantId, EncryptedPayload, KeyId)` → `PayloadJson`
  (`PayloadAvailable = true`). On decrypt failure, catch and return
  `PayloadJson = ""`, `PayloadAvailable = false`.

**Command-type options:** `GetAuditCommandTypesQuery() : IQuery<IReadOnlyList<string>>`, `[RequiresPermission(Permission.AuditRead)]` — `SELECT DISTINCT CommandType FROM AuditEvents ORDER BY CommandType`, so the UI's action filter always matches the data (no brittle curated list).

**Endpoints** (register in `Program.cs`)
```
GET /api/v1/audit?from=&to=&principalId=&commandType=&status=&crossTenantOnly=&page=&pageSize=   → paged list
GET /api/v1/audit/{id}                                                                            → detail (decrypted)
GET /api/v1/audit/command-types                                                                   → distinct command types
```

### Slice B — Web UI

- **Page** `Audit.razor` at `@page "/audit"`, `@attribute [Authorize]`, added
  to the **Access** `MudNavGroup` in `NavMenu.razor` (after Roles).
- **Filter bar:** a date-range (two `MudDatePicker`s), an actor `MudSelect`
  (options = existing `GetMembersAsync` emails + `GetServiceAccountsAsync`
  names, value = principal id), an action `MudSelect` (from
  `command-types`), a status `MudSelect` (Success/Failed/any), a
  cross-tenant `MudSwitch`. Changing a filter reloads page 1.
- **Grid:** `MudDataGrid` with **server-side data** (`ServerData` → the paged
  API), columns Time (`OccurredAt`), Actor (`ActorDisplay`), Action
  (`CommandType`), Status (a colored `MudChip` — green Success / red Failed),
  Scope (project/env id if present, else "—"), IP. A cross-tenant row shows a
  small warning badge.
- **Detail drawer/dialog:** row-click calls `GetAuditEventDetailAsync(id)` and
  shows the full metadata plus the pretty-printed `PayloadJson` (monospace),
  with `[REDACTED]` visible where secrets were scrubbed. If `PayloadAvailable
  == false`, show "Payload unavailable (encryption key rotated away or
  shredded)."
- **Web client:** extend `DeveloperPlatformApiClient` with
  `GetAuditEventsAsync(filter, page, pageSize)`,
  `GetAuditEventDetailAsync(id)`, `GetAuditCommandTypesAsync()`, plus DTOs.
  Read methods swallow HTTP failure to an empty/None result, matching the
  existing list-method convention.
- Global interactive render mode is on `<Routes>` — no per-page `@rendermode`.
  Follow the MudBlazor zinc theme.

## Data flow

1. **List:** page → `GET /api/v1/audit?…` → `GetAuditEventsQuery` → checks
   `audit:read` @ tenant scope → filter + page + actor-join over the columns →
   `PagedResult<AuditEventSummary>` (no decryption).
2. **Detail:** row-click → `GET /api/v1/audit/{id}` → `GetAuditEventDetailQuery`
   → checks `audit:read` → decrypts one payload (or marks unavailable) →
   returns scrubbed JSON.

## Error handling & edge cases

- No `audit:read` → 403 from the dispatcher; the page shows a friendly
  "You don't have permission to view the audit log."
- Detail for a missing id → 404; for an undecryptable payload → 200 with
  `PayloadAvailable = false`.
- Empty result / no events yet → grid empty state.
- `Page`/`PageSize` out of range → clamped server-side.
- Actor whose principal/user was deleted → `ActorDisplay` falls back to the
  raw id (never throws).

## Testing

- **Handler tests:** each filter narrows results; paging returns the right
  slice + correct `Total`; ordering is newest-first; actor join resolves a
  member email and a service-account name, and falls back for an unknown
  principal; detail decrypts the payload; detail on a shredded key returns
  `PayloadAvailable = false` (seed a key, encrypt, `ShredKeyAsync`, assert no
  throw). Command-types query returns distinct sorted values.
- **Authz test (dispatcher-level):** `audit:read` at tenant scope allows the
  list; a principal without it gets `ForbiddenException`.
- **Web client test:** `GetAuditEventsAsync` deserializes items + total.
- Architecture tests stay green; solution builds clean.

## Out of scope (YAGNI)

CSV/JSON export; live tail / streaming; retention or archival; full-text
search over payloads; project/environment scope filter (those columns are
populated only from JWT claims, so they are null for human route-scoped
actions — filtering on them would silently miss most rows); charts/analytics;
per-row "re-run" or diff.
