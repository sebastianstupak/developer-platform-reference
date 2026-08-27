# Developer Platform Reference

A multi-tenant internal developer platform on **.NET 10**: tenants manage **projects, environments, and encrypted secrets**, governed by fine-grained **RBAC** (users, service accounts, API keys), with every privileged action **audited**. Authentication via **Keycloak** (OIDC).

> Reference / learning project. Not production-hardened.

<p align="center">
  <img src="docs/screenshots/project-overview.png" alt="Project overview" width="100%" />
</p>

<table>
  <tr>
    <td width="50%"><img src="docs/screenshots/environment-secrets.png" alt="Environment secrets" /></td>
    <td width="50%"><img src="docs/screenshots/audit-log.png" alt="Audit log" /></td>
  </tr>
</table>

## Stack

.NET 10 · ASP.NET Core Minimal APIs · Blazor Server (MudBlazor) · EF Core 10 + MariaDB (Pomelo) · Keycloak (OIDC / JWT) · RabbitMQ · Redis · xUnit + Playwright

## Architecture

Clean Architecture; dependencies point inward to `Domain`. Boundaries are enforced by `DeveloperPlatform.ArchitectureTests`.

| Layer | Responsibility |
| --- | --- |
| `Domain` | Entities, value objects, invariants (no dependencies) |
| `Application` | Commands / queries + `I{Command,Query}Dispatcher` ports |
| `Infrastructure` | EF Core, crypto, messaging, auth, handlers |
| `Api` | Versioned HTTP API (JWT / API-key), OpenAPI + Scalar |
| `Web` | Blazor Server UI (cookie + OIDC) |

- **Multi-tenancy:** shared tables with an EF Core global query filter on `ITenantScoped`, plus an explicit path for cross-tenant support operations.
- **Secrets:** per-tenant **AES-256-GCM** encryption. Rotated keys are retained so historical audit payloads stay decryptable.
- **Audit:** commands write to a transactional **outbox**, relayed over RabbitMQ to the audit store. Queries never mutate state.
- **Authorization:** permission and resource-scope checks run in the dispatch pipeline (`[RequiresPermission]` / `IResourceScoped`).

## Getting started

Prerequisites: **.NET 10 SDK**, **Docker**.

```bash
docker compose up -d                              # MariaDB, Redis, RabbitMQ, Keycloak (:8090)
dotnet run --project src/DeveloperPlatform.Api    # http://localhost:5274  (Scalar docs at /docs/v1)

# Web must bind :5000; the Keycloak redirect URI is http://localhost:5000/signin-oidc
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5000 \
  ConnectionStrings__Redis=127.0.0.1:6379 Api__BaseUrl=http://127.0.0.1:5274 \
  dotnet run --project src/DeveloperPlatform.Web --no-launch-profile
```

Sign in at <http://localhost:5000> with `dev@example.com` / `password`. The first sign-in becomes the tenant **Owner**.

## Testing

```bash
dotnet test                                            # unit + integration + architecture
cd tests/e2e && npm install && npx playwright test     # e2e (requires the stack running)
```
