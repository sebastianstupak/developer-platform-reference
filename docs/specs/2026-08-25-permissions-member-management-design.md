# Permissions & Member Management — Design Spec

- **Date:** 2026-08-25
- **Status:** Approved (pending written-spec review)
- **Scope:** Phase 4 — the authorization subsystem that must land *before* API key generation, so keys are minted against a real permission model rather than the coarse `ApiKeyScope` flags that exist today.

## 1. Motivation

Today authorization is claim-trust: `ExecutionContextMiddleware` reads `tenant_id`, `sub`, `api_key_id`, `project_id`, `environment_id` straight off the token and trusts them. `ApiKeyScope` is a coarse `[Flags]` enum (`Read`/`Write`/`Admin`), `Tenant` has no members, and there is no DB-backed API-key authentication. There is no layer between "authenticated identity" and "what a key/user may do." This spec introduces a platform-owned RBAC model with per-instance ACLs, a unified principal abstraction (a member and an API key are the same *kind of thing*), and moves enforcement from claim-trust to DB-backed permission checks.

## 2. Decisions

1. **Authorization owner:** platform-owned RBAC. Keycloak authenticates (proves identity) only; the platform stores and enforces its own Member/ServiceAccount/Role/Permission model.
2. **Unified principal:** a **Member** (human) and a **ServiceAccount** (machine) are both **Principals** — the unit that holds permission grants and is named in the audit trail. Audit reads uniformly: "principal `serviceaccount:ci-deployer` did X" vs "principal `member:alice` did X".
3. **Permission granularity:** `resource:action` permissions **plus per-instance ACLs**. Every grant carries a **Scope** (`Tenant` | `Project:{id}` | `Environment:{id}`).
4. **Machine credentials:** a **ServiceAccount** holds the grants; one or more **ApiKeyCredentials** authenticate *as* it. Rotating a key = issue new credential + revoke old; permissions untouched.
5. **Onboarding:** hybrid. A global **User** identity is JIT-created on first Keycloak login; joining a specific tenant requires an **Invitation** (a `User` can belong to several tenants).
6. **Permission catalog representation:** type-safe enums (`Permission`, `Resource`, `Action`) with a metadata attribute; the canonical `resource:action` string is *derived*, never hand-typed. Single source of truth in code.

## 3. Domain model

```
User (global, IEntity)                    ← JIT-created on first Keycloak login; spans tenants
  · Id, KeycloakSubject (unique), Email, DisplayName, CreatedAt

Principal (TenantEntity, abstract)        ← the actor: holds grants + named in audit
  · Id, TenantId, DisplayName, Type (Member | ServiceAccount)
  ├── Membership   : Principal            ← human-in-tenant
  │     · UserId → User, Status (Invited | Active | Suspended)
  └── ServiceAccount : Principal          ← machine
        · Name, Description

ApiKeyCredential (TenantEntity)           ← authenticates AS a ServiceAccount (many-per-account)
  · Id, TenantId, ServiceAccountId → Principal, KeyPrefix, KeyHash,
    ExpiresAt?, IsRevoked, RevokedAt?, LastUsedAt?, CreatedAt

Role                                      ← named bundle of permissions
  · Id, Name, IsSystem, TenantId? (null = system/global)
RolePermission                            ← Role ↔ Permission join
  · RoleId, Permission

RoleAssignment    · Id, TenantId, PrincipalId, RoleId,      Scope
PermissionGrant   · Id, TenantId, PrincipalId, Permission,  Scope   ← direct/ACL grant outside a role

Invitation (TenantEntity)                 ← hybrid onboarding bridge
  · Id, TenantId, Email, RoleId, Scope, Token, Status (Pending|Accepted|Revoked|Expired), ExpiresAt, CreatedAt
```

**Scope** is a value object: `ScopeType` (`Tenant`/`Project`/`Environment`) + optional target `Guid`. Hierarchy: `Tenant ⊇ Project ⊇ Environment`.

**Modeling notes**
- `User` is global (`IEntity`, like `Tenant`); everything else is tenant-scoped (`TenantEntity`).
- `Principal` uses table-per-hierarchy (single table, `Type` discriminator) unless TPT proves cleaner during implementation.
- Built-in system roles have `TenantId = null` and `IsSystem = true`; tenant-custom roles are deferred (the schema already allows them).

## 4. Permission catalog (single source of truth)

```csharp
public enum Resource { Projects, Secrets, ApiKeys, Members, Roles, ServiceAccounts, Audit }
public enum Action   { Read, Write, Manage, Delete }

[AttributeUsage(AttributeTargets.Field)]
public sealed class PermAttribute(Resource resource, Action action, string description) : Attribute
{
    public Resource Resource    { get; } = resource;
    public Action   Action      { get; } = action;
    public string   Description { get; } = description;
}

public enum Permission
{
    [Perm(Resource.Projects, Action.Read,   "View projects")]         ProjectsRead,
    [Perm(Resource.Projects, Action.Write,  "Create/edit projects")]  ProjectsWrite,
    [Perm(Resource.Secrets,  Action.Read,   "Read secret values")]    SecretsRead,
    [Perm(Resource.Secrets,  Action.Write,  "Set/rotate secrets")]    SecretsWrite,
    [Perm(Resource.ApiKeys,  Action.Manage, "Manage API keys")]       ApiKeysManage,
    [Perm(Resource.Members,  Action.Manage, "Invite/remove members")] MembersManage,
    // ...extended as features land (Roles.Assign, ServiceAccounts.Manage, Audit.Read, ...)
}
```

- Enums are compile-time constants, so `[RequiresPermission(Permission.SecretsWrite)]` is valid and type-safe.
- At startup a `PermissionCatalog` reflects over `Permission` **once**, producing `Permission → PermissionDescriptor { Resource, Action, Description, Token }`.
- Canonical `Token` = `resource.ToString() + ":" + action.ToString()`, lowercased (e.g. `"secrets:write"`). This token is what persists in the DB (EF `HasConversion`) and what the `/permissions` endpoint exposes.
- **Rename stability:** because the token derives from enum member *names*, `Resource`/`Action` support an optional `[Token("service-accounts")]` override used only when a member's wire name must differ from its C# identifier. Default path derives automatically — no hand-typed strings.

## 5. Authentication → principal resolution

Two authentication schemes collapse to a single `PrincipalId` used everywhere downstream.

**Machine path (new):** an `ApiKeyAuthenticationHandler` reads the key from a header (`X-Api-Key` / `Authorization: Bearer dpk_live_…`), splits the prefix, hashes the secret, looks up a live `ApiKeyCredential` (not revoked, not expired), then sets `PrincipalId = ServiceAccount.Id`, `PrincipalType = ServiceAccount`, **TenantId derived from the credential** (not a claim), and calls `RecordUsage()`. Replaces the `api_key_id`-claim shortcut.

**Human path (evolved OIDC):** Keycloak authenticates. On the token `sub`, **find-or-JIT-create** a `User` keyed by `KeycloakSubject`. Resolve the **active `Membership`** for the current tenant → `PrincipalId = Membership.Id`, `PrincipalType = Member`. First-login invitation activation: match a `Pending` `Invitation` by email → create `Membership` (Active) with the invited role/scope → mark `Accepted`.

**Execution context change (breaking, intentional):** `IExecutionContext` drops `UserId`/`ApiKeyId`, gains `PrincipalId` + `PrincipalType`, and keeps `UserId` only as the *nullable human behind a membership* (null for service accounts) for audit readability. `TenantId`/`ProjectId`/`EnvironmentId`/`IpAddress` stay. `ExecutionContextMiddleware` now branches by auth scheme instead of unconditionally requiring a `tenant_id` claim.

**Tenant selection (v1):** for humans, tenant continues to arrive as an OIDC claim (Keycloak-driven, as today), validated against an active `Membership`. Machine calls derive tenant from the credential. An in-app tenant switcher for multi-tenant users is **out of scope** for this spec.

## 6. Authorization enforcement

**Single enforcement point — the dispatcher.** A `[RequiresPermission(Permission.X)]` attribute on a command/query is read by reflection in `CommandDispatcher`/`QueryDispatcher` (the same place `SkipAuditAttribute` is read today) and checked *before* the handler runs. Deny → `ForbiddenException` → HTTP `403`. Because the Blazor app calls the API over HTTP (`DeveloperPlatformApiClient`), this one point authoritatively covers web and machine callers.

**Per-instance scope resolution.** A guarded command may implement `IResourceScoped` exposing its target `Scope` (e.g. `DeleteProjectCommand` → `Project:{id}` from its payload). Check scope = command's `IResourceScoped` scope → else execution-context `Project/Environment` → else `Tenant`. `IAuthorizationService.Authorize(principalId, permission, scope)` gathers role assignments + direct grants at that scope **and its ancestors**, expands roles → permissions, allows only if present.

**Privilege-escalation guard.** Management commands are themselves guarded (`members:manage`, `apikeys:manage`, `serviceaccounts:manage`, role assignment). Rule: **a principal can only grant a role/permission it itself holds at that scope** — `AssignRole`/`GrantPermission`/mint-key handlers verify the actor's effective permissions are a superset of what is handed out. A credential can never exceed its service account; no principal can escalate itself.

**Bootstrapping.** System roles (Owner/Admin/Developer/Viewer) are seeded. Creating a tenant makes the creator an **Owner `Membership` at tenant scope** — the root of trust that can then invite others.

**Audit touch-up.** `AuditOutboxEntry` + `BuildOutboxEntryAsync` swap `userId`/`apiKeyId` for `principalId` + `principalType` (human `userId` kept nullable). Denied authorizations are audited as `Forbidden`.

### Built-in role → permission matrix (seed)

| Role      | Permissions (at tenant scope) |
|-----------|-------------------------------|
| Owner     | all permissions               |
| Admin     | all except tenant/ownership transfer |
| Developer | `projects:*`, `secrets:*`, read-only members |
| Viewer    | `*:read` |

(Exact matrix finalized during implementation as the catalog grows.)

## 7. API surface

All endpoints `RequireAuthorization` and carry `[RequiresPermission]` on their command/query. Member and service-account *lifecycle* is per-type; *grants* are shared under `/principals`.

**Catalog (read):**
- `GET /api/v1/permissions` — the `PermissionCatalog`
- `GET /api/v1/roles` — built-in roles + their permissions

**Members** (`members:manage`):
- `GET /api/v1/members`
- `POST /api/v1/members/invitations` — email + role + scope
- `DELETE /api/v1/members/invitations/{id}` — revoke invite
- `DELETE /api/v1/members/{id}` — remove/suspend membership
- Acceptance is implicit (JIT-matched on first login)

**Service accounts** (`serviceaccounts:manage`):
- `GET|POST|DELETE /api/v1/service-accounts`

**API keys** (`apikeys:manage`):
- `POST /api/v1/service-accounts/{id}/keys` → returns the secret **once**
- `GET  /api/v1/service-accounts/{id}/keys` — metadata only, never the secret
- `POST /api/v1/service-accounts/{id}/keys/{keyId}/revoke`

**Shared grants** (subject to escalation guard):
- `POST|DELETE /api/v1/principals/{id}/role-assignments` — role + scope
- `POST|DELETE /api/v1/principals/{id}/permission-grants` — permission + scope
- `GET /api/v1/principals/{id}/effective-permissions?scope=` — UI + debugging

## 8. Web UI

MudBlazor, following the existing `Projects.razor` + `IDialogService` interactive pattern. New **Access** section in `NavMenu`:
- **Members** — table (status, roles), invite dialog (email + role + scope), change role, remove
- **Service Accounts** — list, create, key management (mint → show-once secret dialog, revoke), assign roles
- **Roles** — read-only view of built-in roles and their permissions

UI is permission-aware (hide/disable actions the current principal lacks) but the API dispatcher remains the sole authority; 403s render gracefully.

## 9. Testing

- **Unit:** `PermissionCatalog` derivation (token/round-trip); effective-permission resolution (scope cascade + role expansion); privilege-escalation guard.
- **Authorization:** extend `ProjectsAuthorizationTests`/`ApiAuthorizationTests` to assert deny-without-permission and allow-with-permission at each scope.
- **API-key auth:** `ApiKeyAuthenticationHandler` — valid / revoked / expired / unknown credential.
- **Onboarding:** invitation activation on first login; JIT user creation.
- **Architecture:** existing `ArchitectureTests` keep layer boundaries honest for the new types.

## 10. Migration & cut-over

Pre-release platform (single `InitialCreate` migration, no production data) → **clean replacement migration**, not data-preserving.

- **New tables:** Users, Memberships, ServiceAccounts (via Principal TPH), ApiKeyCredentials, Roles, RolePermissions, RoleAssignments, PermissionGrants, Invitations.
- **Replaced:** the old `ApiKey` entity and `ApiKeyScope` flags → ServiceAccount + ApiKeyCredential + grants. The existing `CreateApiKeyCommand`/`CreateApiKeyEndpoint` are reworked to the service-account model.
- **Audit:** `AuditOutboxEntry`/`AuditEvent` swap `userId`/`apiKeyId` → `principalId`/`principalType`.
- **Seed:** the four system roles and their permission sets.

## 11. Suggested implementation order

1. Permission catalog (`Permission`/`Resource`/`Action` + `PermissionCatalog`) + principal/role/grant domain model + EF config/migration + `IAuthorizationService` + dispatcher enforcement + `IExecutionContext` change + audit touch-up.
2. `ApiKeyAuthenticationHandler` + ServiceAccounts + ApiKeyCredentials (mint/list/revoke) — replaces the old ApiKey path.
3. Member invitations + hybrid onboarding (JIT user, invitation activation) + shared grant endpoints.
4. Web **Access** UI (Members, Service Accounts, Roles).

Each step is independently testable and shippable.

## 12. Out of scope (future specs)

- In-app tenant switcher for multi-tenant users.
- Tenant-custom roles (schema already permits; UI/endpoints deferred).
- Per-instance ACL management UI beyond scope pickers (bulk ACL editing, inheritance visualization).
- Rate-limiting API keys (Redis is provisioned but unused by the API today).
- `DatabasePerTenant` tenancy mode interaction with the above.
