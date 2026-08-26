# Web Access UI Implementation Plan (Authz Slice 6 of 6 — final)

**Goal:** Add MudBlazor pages to the Blazor Web app for managing access — Roles, Members (+ invitations), and Service Accounts (+ API keys) — consuming the Slice 1-5 APIs, consistent with the existing zinc-theme dashboard.

**Design decision:** MATCH the existing dashboard exactly (established Phase-3 MudBlazor zinc theme + `Projects.razor` pattern: `MudStack` header + `MudDataGrid` + `NoRecordsContent` empty states, `MudDialog` create dialogs, `ISnackbar` feedback, `DeveloperPlatformApiClient` over HTTP with try/catch). For an internal platform's access-management, consistency is the correct design choice — the visual identity was already set. UI copy: active voice, plain terms, sentence case, empty states that invite action.

**Verification:** Blazor pages are verified by BUILD + a live **Playwright** pass (log in via Keycloak as Owner, screenshot each page, drive an invite and a create-service-account/issue-key flow). No bUnit harness exists; the Web.Tests project covers the API client/token handler.

**Approach:** Built directly (not subagent-dispatched) because UI needs visual iteration via rendered screenshots.

## Where this fits

**Slice 6 of 6 (final).** Backend (Slices 1-5) is merged + e2e-verified. This slice is frontend wiring + one small backend read endpoint.

## Tasks

### Task 1 — Backend: `GET /api/v1/service-accounts` (list)
The Service Accounts page needs to list service accounts; Slice 4 only added create + per-SA key ops. Add:
- `GetServiceAccountsQuery : IQuery<IReadOnlyList<ServiceAccountSummary>>` `[RequiresPermission(ServiceAccountsManage)]` → `ServiceAccountSummary(Guid PrincipalId, string Name, string? Description, DateTime CreatedAt)` (Application).
- Handler (Infrastructure): join `ServiceAccounts` (tenant-filtered). Register.
- Endpoint `GET /api/v1/service-accounts` in `ServiceAccountsEndpoints`.

### Task 2 — Web API client + DTOs
Add Web-local DTOs (`Http/Models/`) mirroring the API JSON (enums/tokens as strings): `RoleDto(Id, Name, string[] Permissions)`, `MemberDto(PrincipalId, UserId, Email, DisplayName, Status)`, `InvitationDto(Id, Email, RoleId, Status, ExpiresAt)`, `ServiceAccountDto(PrincipalId, Name, Description, CreatedAt)`, `ApiKeyDto(Id, Name, KeyPrefix, ExpiresAt, IsRevoked, LastUsedAt, CreatedAt)`, `PermissionDto(Token, Resource, Action, Description)`, `IssuedKeyDto(CredentialId, PlaintextKey, KeyPrefix)`.
Add `DeveloperPlatformApiClient` methods (same try/catch idiom): `GetRolesAsync`, `GetMembersAsync`, `GetInvitationsAsync`, `InviteMemberAsync(email, roleId)`, `RevokeInvitationAsync(id)`, `GetServiceAccountsAsync`, `CreateServiceAccountAsync(name, description, grants)`, `GetApiKeysAsync(saId)`, `IssueApiKeyAsync(saId, name)`, `RevokeApiKeyAsync(saId, credId)`, `GetPermissionsAsync`.

### Task 3 — Roles page (`/roles`, read-only)
Table of roles with a permissions chip-set per role. Simplest page.

### Task 4 — Members page (`/members`)
Members table (email, status). "Invite member" dialog (email + role dropdown from `GetRolesAsync`, tenant scope). Pending invitations table (email, role, status, expires) with revoke.

### Task 5 — Service Accounts page (`/service-accounts`)
Service accounts table (name, description). "New service account" dialog (name + description + permissions multi-select from `GetPermissionsAsync`, tenant scope). Expandable per-SA keys: issue key (→ show-once secret dialog), list (prefix, expiry, last-used, revoked), revoke.

### Task 6 — NavMenu + Playwright verification
Add nav links (Roles, Members, Service Accounts) grouped under "Access". Bring up the stack + Web, log in, screenshot each page, drive an invite + a create-SA/issue-key flow.

## Notes
- Enums over the wire are STRINGS (Slice 5 `JsonStringEnumConverter`); Web DTOs use strings.
- Token flow: `PersistentComponentState` "AccessToken" → `TokenProvider` (as in `Projects.razor`).
- Permission-aware UI (hide/disable) is a nice-to-have; the API dispatcher is the sole authority (403 renders as a snackbar error). For v1, show actions and surface failures via snackbar.
