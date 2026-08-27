# Projects & Environments UX + Global Switcher — Design

**Date:** 2026-08-27
**Status:** Approved (design), pending spec review
**Phase:** 7 (UX)

## Goal

Make navigating projects and switching environments fast and pleasant: a persistent
project/environment **switcher** in the app bar, a **card-grid** projects list, a new
**project Overview** page, and a reworked **environment secrets** view. Add the minimal
backend enrichment these screens require.

## Non-goals

- No changes to Members, Service accounts, Roles, or the standalone Audit page beyond a
  single additive query filter.
- No new permissions or authorization model changes — existing `[RequiresPermission]`
  gates and the tenant query filter are reused as-is.
- No redesign of the secrets grid mechanics (reveal/edit/delete) — it is relocated, not
  rebuilt.

---

## A. Active context (core new concept)

A per-circuit **scoped** service `ActiveContextService` (registered in the Web project's
DI) holds the active project and environment and notifies subscribers.

```csharp
public sealed class ActiveContextService
{
    public ActiveProjectRef? Project { get; private set; }      // (Id, Name)
    public ActiveEnvironmentRef? Environment { get; private set; } // (Id, Name, Type)
    public event Action? OnChange;

    public void SetProject(ActiveProjectRef? project);   // clears Environment if project changes
    public void SetEnvironment(ActiveEnvironmentRef? env);
    public void Clear();
}

public readonly record struct ActiveProjectRef(Guid Id, string Name);
public readonly record struct ActiveEnvironmentRef(Guid Id, string Name, string Type);
```

**Design rule — route is the source of truth:**

- Pages push their project/env *into* the context on load (`OnParametersSetAsync`), so the
  app-bar labels always match what the user is viewing — including deep links and card
  clicks.
- The switcher is a **fast navigator**: selecting an item calls
  `NavigationManager.NavigateTo(...)`; the destination page then syncs the context. The
  switcher never mutates page data directly.

Within a Blazor Server circuit this already "remembers across navigation" (the scoped
service outlives page navigations). Cross-reload restore is a small, isolated add:
persist the last `{projectId, environmentId}` via `ProtectedLocalStorage`, and on the
first interactive render of a page with no route project, offer it as the switcher's
default. This piece is **trimmable** — see Scope.

`OnChange` subscribers must unsubscribe in `Dispose` to avoid circuit leaks.

---

## B. Information architecture (routes)

| Route | Page | Notes |
|-------|------|-------|
| `/projects` | **Projects list** (card grid) | Landing / Projects nav item |
| `/projects/{projectId:guid}` | **Project Overview** *(new)* | Environments at a glance + recent activity |
| `/projects/{projectId:guid}/environments/{environmentId:guid}` | **Environment secrets** *(reworked ProjectDetail)* | Deep-linkable; tab strip removed |

The current `ProjectDetail.razor` (route `/projects/{id}`, env tabs + secrets grid) is
**split** into Overview + Environment secrets. The env tab strip is removed; the app-bar
environment picker is now the way to switch environments.

---

## C. Components & pages

### C1. `ContextSwitcher.razor` (app bar)
- Layout: `[Project ▾] ▸ [Environment ▾]` rendered in `MainLayout` app bar.
- Both pickers searchable (MudAutocomplete + selection, the pattern established in the
  audit filters work).
- Project picker: lists all projects (`GetProjectsAsync`); select → `SetProject` +
  navigate to that project's Overview.
- Environment picker: enabled only when a project is active; lists that project's
  environments (`GetEnvironmentsAsync`); select → navigate to the secrets view.
- Responsive: below the drawer breakpoint (960px) collapse to the project picker only;
  the environment picker moves into an overflow menu.
- Subscribes to `ActiveContextService.OnChange` to keep labels current.

### C2. `Projects.razor` — card grid (rework)
- Responsive `MudGrid` of project cards. Each card: name, truncated description,
  **environment count**, **"updated" relative time**; hover elevation; click → Overview.
- "New project" affordance (button or trailing card) reusing the existing create dialog
  (`IDialogService`, MudBlazor v9).
- Empty state: "Create your first project" call to action.
- Requires `EnvironmentCount` + `LastActivityAt` on the projects DTO (§D1).

### C3. `ProjectOverview.razor` — new page
- Header: project name, description, quick actions — **New environment**, **New secret**
  (targets the active/first env), and a project actions menu with **Delete project**
  (reuses the existing delete endpoint). No project *rename* — the API exposes no project
  update endpoint, so it is out of scope.
- **Environment cards**: one per environment — name, **type badge**, **secret count**,
  last-updated; click sets the active env and navigates to the secrets view. Trailing
  "New environment" affordance.
- **Recent activity**: the last N (default 8) audit events for this project — time,
  actor, action, status — with a "View all in Audit" link to `/audit`. Hidden with a
  quiet empty state when there are none or the user lacks audit read.
- Requires `SecretCount`/`LastUpdatedAt` on the environments DTO (§D2) and a `projectId`
  audit filter (§D3).

### C4. `EnvironmentSecrets.razor` — reworked ProjectDetail
- Header: back-to-Overview, `project · environment` label, environment **type badge**,
  and the **rename / delete environment** actions relocated from the old tab strip.
- Body: the existing secrets `MudDataGrid` (Name / Updated + reveal / edit / delete) and
  **Add secret**, reused as-is. Empty state preserved.
- Reads `ProjectId` + `EnvironmentId` from the route; pushes both into `ActiveContext`.

---

## D. Backend enrichment (CQRS, existing conventions)

Three additive changes. Each keeps the existing query name and adds fields/filters; no new
query types except where noted. The UI cannot render these screens on today's DTOs without
N+1 fan-out, so the enrichment is load-bearing, not gold-plating.

### D1. Projects list counts
- `ProjectSummary` and `ProjectResponse`/`ProjectDto` gain **`int EnvironmentCount`** and
  **`DateTime LastActivityAt`**.
- `GetProjectsQuery` handler computes them: `EnvironmentCount` = count of the project's
  environments; `LastActivityAt` = `MAX(secret.UpdatedAt)` across the project's secrets,
  falling back to `project.CreatedAt` when the project has no secrets.
- Implemented as grouped projections in the existing query (no per-project round-trips).

### D2. Environment secret counts
- `EnvironmentSummary` and `EnvironmentResponse`/`EnvironmentDto` gain
  **`int SecretCount`** and **`DateTime LastUpdatedAt`** (`MAX(secret.UpdatedAt)` for the
  env, falling back to `env.CreatedAt`).
- `GetEnvironmentsQuery` handler computes them via a grouped projection.
- Overview's "total secrets" is the sum of `SecretCount`; no separate aggregate needed.

### D3. Project-scoped recent activity
- `AuditFilter` gains an optional **`Guid? ProjectId`**; `GetAuditEventsQuery` handler adds
  `where e.ProjectId == filter.ProjectId` when set. `AuditEvent.ProjectId` already exists.
- Audit endpoint `GET /api/v1/audit` gains a `Guid? projectId` query parameter, threaded
  into `AuditFilter`.
- Web: `AuditFilterDto` gains `ProjectId`; `BuildAuditQuery` emits `projectId=` when set.
  The Overview calls the existing `GetAuditEventsAsync` with `ProjectId` set and a small
  `pageSize`. No new client method.

---

## E. Visual language

Reuse the zinc theme (`DevPlatformTheme`): borders `LinesDefault` (#e4e4e7), 0.5rem radius,
subtle hover elevation, Inter type. Environment-type badges as colored chips
(production / staging / development). Relative timestamps via a small shared helper
(`RelativeTime(DateTime)` → "2d", "5h", "just now"). The card + overview visual pass is a
genuine design task — the implementation plan will invoke the **frontend-design** skill for
that step so the grid and overview get a deliberate treatment rather than default MudBlazor.

---

## F. Structural moves (requested)

Two isolated housekeeping tasks, done early and independently of the UX work:

1. **`keycloak/` → `infra/keycloak/`**
   - `git mv keycloak infra/keycloak`.
   - `docker-compose.yml` lines 57–58: `./keycloak/realm-export.json` →
     `./infra/keycloak/realm-export.json`, and `./keycloak/themes/devplatform` →
     `./infra/keycloak/themes/devplatform`.
   - Sweep for other references (README, docs, run-recipe notes) and update.
   - The realm export references the theme by name (`devplatform`), not path — unaffected.

2. **`e2e/` → `tests/e2e/`**
   - `git mv` the tracked files (`package.json`, `playwright.config.js`, `tests/`,
     `.gitignore`, `README.md`). `node_modules` is gitignored and regenerated with
     `npm install`.
   - `playwright.config.js` `baseURL` and the absolute Chromium `executablePath` are
     unaffected by the move.
   - New UX e2e specs land in `tests/e2e/tests/`.

---

## G. Testing

- **Backend:** unit/integration tests for the three enriched queries — `EnvironmentCount`
  and `LastActivityAt` on projects; `SecretCount` and `LastUpdatedAt` on environments;
  the `projectId` audit filter (matching + non-matching events). Follow the existing
  in-memory `ApplicationDbContext` harness.
- **e2e (Playwright, `tests/e2e/`):** switcher flow (pick project → Overview → pick env →
  secrets), card grid renders counts, Overview shows env cards + recent activity. Reuse
  the established config (headed via `HEADLESS`, cached Chromium `executablePath`).
- Web component logic stays thin; UI flows are covered by e2e, data shapes by backend
  tests.

---

## H. Scope & trimmable bits

One coherent feature, sizable: 1 new service, 1 new page + 2 reworked pages, 1 shared
component, 3 backend enrichments, 2 structural moves. If leaner is wanted:

- **Recent activity** on the Overview (§C3, §D3) can be dropped — removes the audit-filter
  change entirely.
- **Cross-reload persistence** (§A, `ProtectedLocalStorage`) can be dropped — the switcher
  still remembers within a circuit.

## I. Risks

- **Route/context desync** — mitigated by the route-as-truth rule (pages always push into
  context on load; switcher only navigates).
- **`OnChange` subscription leaks** — every subscriber unsubscribes in `Dispose`.
- **`LastActivityAt` on projects with no secrets** — explicit fallback to `CreatedAt`
  avoids null/`MinValue`.
- **Splitting ProjectDetail** — the reworked env secrets view must preserve all existing
  secret mechanics and the persisted-token restore that the current page performs.
