# E2E tests as a .NET project (Playwright + Testcontainers)

**Date:** 2026-08-28
**Status:** Approved design

## Context

End-to-end tests currently live in `tests/e2e/` as a standalone **Node** project using
`@playwright/test` (7 tests across `audit-filters.spec.js` and `projects-switcher.spec.js`).
They drive the Web UI at `http://localhost:5000` against a **live, manually-started** stack
(`docker compose up`, then the API on `:5274` and Web on `:5000`, logging in through Keycloak
as `dev@example.com`), and they assume data (e.g. a `payments-api` project) that only exists
because it was created by hand in the dev database.

This design replaces that suite with a **first-class .NET test project** that is part of the
solution, uses **Playwright for .NET**, and provisions its own backing services with
**Testcontainers** so a run is self-contained rather than depending on a hand-managed stack.

## Goals

- E2E tests are a normal `dotnet test` project inside `developer-platform-reference.slnx`.
- A run brings up its own MariaDB / RabbitMQ / Redis / Keycloak via Testcontainers — no manual
  `docker compose` step.
- Deterministic seed data, so assertions don't depend on a hand-populated database.
- The existing 7 test scenarios are ported faithfully (same flows, same selectors).
- CI stays green and fast — e2e does **not** run in the normal CI test job.

## Non-goals

- Running e2e in CI (deferred; a dedicated CI job can be added later).
- Containerizing the API/Web apps (no Dockerfiles are added; they run as child processes).
- Parallel e2e runs / sharding.
- Converting the existing unit/integration test projects to Testcontainers.

## Decisions

1. **Framework:** xUnit + `Microsoft.Playwright` (matches the repo's three existing xUnit
   projects; a small custom fixture replaces the NUnit/MSTest `PageTest` base class).
2. **Backing services:** Testcontainers.
3. **Seed data:** the e2e **fixture seeds via the API** after a real `dev@example.com` login — no
   in-app seeder and no `src/` change. Requires one infra change: enable **Direct Access Grants** on
   the realm's public `cli-client` so the fixture can obtain a token (password grant).
4. **CI:** local/on-demand only; excluded from the normal CI test run via a trait filter.
5. **Old suite:** the Node `tests/e2e/` project is removed.

> Note: an earlier draft used an in-app Development seeder. That was dropped because the command
> pipeline reads the tenant/principal from a request-scoped execution context populated by the real
> login (JWT `tenant_id` claim), and the Owner is JIT-provisioned by `PrincipalResolver` on first
> login. Seeding through the real login + API avoids replicating JIT provisioning, matching the
> Keycloak user identity, and faking the execution context — and it generates audit events naturally.

## Architecture

### New project

`tests/DeveloperPlatform.E2ETests/` — xUnit, `net10.0` — added to
`developer-platform-reference.slnx` under the `/tests/` folder.

Package references mirror the existing test projects (`Microsoft.NET.Test.Sdk`, `xunit`,
`xunit.runner.visualstudio`, `coverlet.collector`) plus:

- `Microsoft.Playwright`
- `Testcontainers`, `Testcontainers.MariaDb`, `Testcontainers.Redis`, `Testcontainers.RabbitMq`
  (Keycloak is built with the generic `ContainerBuilder`, mounting the existing realm export).

It references `DeveloperPlatform.Infrastructure` (to apply EF migrations against the DB
container via the real `DbContext`).

### Stack fixture

`AppStackFixture : IAsyncLifetime`, shared across the suite via an xUnit
`[CollectionDefinition("app-stack")]`. Lifecycle:

**Init**
1. Start containers: MariaDB, Redis, RabbitMQ on **random** host ports; Keycloak on a **fixed**
   `8090:8080`, mounting `infra/keycloak/realm-export.json` and the `devplatform` theme, command
   `start-dev --import-realm`, waited on its OIDC discovery document.
2. Apply EF migrations to the MariaDB container (`DbContext.Database.MigrateAsync()` using the
   container's connection string).
3. Launch the **API** (`:5274`) and **Web** (`:5000`) as child `dotnet` processes, environment-
   wired to the container endpoints (see *Config wiring*). Poll each for readiness.
4. The API's dev seeder runs on startup (flag set by the fixture) and populates the sample data.
5. Ensure the Playwright browser is installed (managed install on first run).

**Dispose**: kill the Web and API processes (whole process tree), then dispose all containers.

**Fixed-port rationale & consequence:** Keycloak's realm hardcodes `http://localhost:5000`
redirect/root/post-logout URIs, so the Web app must answer on `:5000` and Keycloak must be
reachable at the Authority the realm/issuer expects (`:8090`). Therefore only **one e2e run at a
time**, and any manually-running stack on `:5000`/`:8090` must be stopped first. DB/Redis/RabbitMQ
stay on random ports (their endpoints are injected into the apps), preserving isolation there.

### Config wiring (env overrides passed to the child processes)

| App | Setting | Source |
| --- | --- | --- |
| API + Web | `ASPNETCORE_ENVIRONMENT=Development` | fixed |
| API | `ASPNETCORE_URLS=http://localhost:5274` | fixed |
| Web | `ASPNETCORE_URLS=http://localhost:5000` | fixed (OIDC redirect constraint) |
| API | DB connection string | MariaDB container |
| API + Web | `ConnectionStrings__Redis` | Redis container |
| API | RabbitMQ connection | RabbitMQ container |
| API + Web | `Keycloak__Authority=http://localhost:8090/realms/developer-platform` | fixed Keycloak |
| Web | `Api__BaseUrl=http://127.0.0.1:5274` | fixed |
| Web | `Keycloak__ClientSecret=web-client-secret` | from realm |
| API | `ConnectionStrings__Default` | MariaDB container |

Concrete keys (confirmed in the codebase): DB is `ConnectionStrings:Default` (Pomelo `UseMySql`),
Redis is `ConnectionStrings:Redis`, RabbitMQ is a hostname (`localhost`, default port 5672).
`127.0.0.1` (not `localhost`) is used for Redis/API to avoid intermittent IPv6 resolution.

### Fixture-driven seeding via the API

After the stack is up, the fixture seeds through the real HTTP pipeline (no `src/` change):

1. **Enable Direct Access Grants** on the realm's public `cli-client` (a one-line change in
   `infra/keycloak/realm-export.json`; both clients ship with it `false`). This lets the fixture get
   a token without a browser.
2. **Get a token** — password grant against `cli-client` for `dev@example.com` / `password` at the
   realm token endpoint. The token carries the hardcoded `tenant_id`
   claim `00000000-0000-0000-0000-000000000001`.
3. **Seed via API** with the bearer token: `POST /api/v1/projects` (`payments-api` plus one more),
   then `POST` environments and secrets. The first authorized call JIT-provisions the Owner exactly
   like a real login (`PrincipalResolver` first-member → Owner), and every write generates audit
   events through the command pipeline — so the audit-filter tests have real data.

The seed helper is idempotent (skips projects that already exist) so re-runs against a warm DB are
safe.

### Test structure

- `E2ETestBase` (in `[Collection("app-stack")]`) holds one shared `IBrowser` (Chromium) from the
  fixture and creates a fresh `IBrowserContext` + `IPage` per test (`BaseURL = http://localhost:5000`,
  `Trace = retain-on-failure`), disposing them after each test.
- A `LoginAsync(page)` helper mirrors the Keycloak form login (`#username`, `#password`, `#kc-login`).
- Ported test classes, tagged `[Trait("Category", "E2E")]`:
  - `AuditFiltersTests` — Action multi-select, Status multi-select, Actor search chip (3).
  - `ProjectsSwitcherTests` — cards render, card→overview→secrets navigation, app-bar combobox
    search/switch, mobile context dialog (4).
- Selectors are ported verbatim (`.project-card`, `.dp-combobox__trigger`,
  `.dp-combobox-popover.mud-popover-open`, `.dp-command__search`, `.dp-command__item`,
  `.env-card`, `.dp-ctxbtn`, `.dp-ctxdlg`, `.mud-list-item`, grid `tbody tr td:nth-child(n)`).

## CI & local run

- `.github/workflows/ci.yml` test step changes to
  `dotnet test developer-platform-reference.slnx --no-build -c Release --filter "Category!=E2E"` so
  the e2e tests are skipped in CI (which has no browser/Keycloak/full stack). Build/format steps are
  unaffected. Because e2e is skipped, the CI job does **not** need any new services.
- Locally (Docker Desktop required): `dotnet test tests/DeveloperPlatform.E2ETests`.
- The pre-commit hook builds the whole solution, so the new project must build cleanly.

## Removal

Delete the Node suite: `tests/e2e/package.json`, `package-lock.json`, `playwright.config.js`,
`tests/*.spec.js`, `README.md`, `.gitignore`. Untracked scratch `*.mjs` files under `tests/e2e/`
are not part of the suite and are left to the developer to discard.

## Risks & mitigations

- **Keycloak issuer/hostname on the fixed port.** Dev-mode Keycloak behind a mapped port can emit an
  issuer that mismatches the Authority. Mitigate with explicit `KC_HOSTNAME*` settings so the issuer
  is `http://localhost:8090/...`; verify token validation succeeds end-to-end.
- **Child-process lifecycle on Windows.** Readiness must be polled (health/OIDC endpoints), and
  teardown must kill the whole process tree to avoid orphaned `dotnet` processes holding `:5000`.
- **First-run slowness.** Image pulls + Keycloak startup (~30–45s) + browser install make the first
  run slow; acceptable for an on-demand suite. Reuse a single browser across tests.
- **Docker requirement.** Testcontainers needs a running Docker engine locally; documented in the
  project README.

## Acceptance criteria

- `tests/DeveloperPlatform.E2ETests` is in the solution and builds under the pre-commit hook.
- `dotnet test tests/DeveloperPlatform.E2ETests` starts the Testcontainers stack, seeds data,
  launches API+Web, and the **7 ported tests pass** with no manual `docker compose`/app steps.
- `dotnet test developer-platform-reference.slnx --filter "Category!=E2E"` (the CI invocation) runs
  the existing suites and **does not** execute any e2e test.
- The Node `tests/e2e/` project is removed.
- `infra/keycloak/realm-export.json` enables Direct Access Grants on `cli-client`, and the fixture's
  password-grant token is accepted by the API (seeded projects are visible via `GET /api/v1/projects`).
