# Production Hardening Notes

This is a reference / learning project and is **not production-hardened**. The local stack takes deliberate dev-only shortcuts. This document enumerates the gaps and what a production deployment would need, so the shortcuts are explicit rather than hidden.

## Keycloak

| Dev (now) | Production |
| --- | --- |
| `start-dev --import-realm` | `start` (production mode) behind TLS, with `KC_HOSTNAME` and `KC_PROXY` set for the real domain |
| Ephemeral H2 database (state lost on container recreate) | External database (`KC_DB`, e.g. Postgres/MariaDB) with backups |
| `KEYCLOAK_ADMIN=admin` / `admin` | Bootstrap admin removed after first run; admins federated or strong-secret |
| Confidential client secret `web-client-secret` committed in `appsettings.Development.json` and `realm-export.json` | Per-environment secret injected from a secret store; never committed |
| `--import-realm` (imports only if the realm is absent) | Realm managed via versioned, idempotent config (e.g. `kcadm`/IaC); import-if-absent silently ignores later drift |
| Redirect URIs allow `http://localhost:5000` **and** `http://127.0.0.1:5000` for dev host flexibility | Only the real HTTPS origin(s); no `http`, no loopback |
| Theme served from a bind mount, `start-dev` theme cache off | Theme baked into the image or a provider; theme cache on |

## Database migrations

- The API does **not** migrate at startup (no `Migrate()`/`EnsureCreated()` in `Program.cs`). Schema is applied out-of-band via `dotnet ef database update` (CI does this before tests).
- Production needs an explicit migration step in the deploy pipeline — a one-shot job (or an EF migration bundle: `dotnet ef migrations bundle`) that runs before the new app version starts. Do not enable migrate-on-startup for multi-instance deployments (racing migrators).

## Secrets encryption master key

- `Crypto:MasterKey` is read from configuration; `appsettings.json` ships a hardcoded dev value (`00…01`). Every tenant data-key is wrapped by this master key.
- Production must source the master key from a KMS / secret manager (e.g. cloud KMS, Vault), never from `appsettings`. Rotating it is a separate exercise from the per-tenant key rotation already implemented — the master key wraps the tenant keys, so master-key rotation means re-wrapping, not re-encrypting secrets.

## Transport & configuration

- All local traffic is `http` (app `:5000`, API `:5274`, Keycloak `:8090`). Production requires HTTPS end to end; OIDC over plain http is dev-only.
- Committed dev credentials — DB `app`/`app`, the Keycloak client secret, the master key — are all placeholders. Production values come from the environment / a secret store.
- `localhost` vs `127.0.0.1`: locally, use `127.0.0.1` for Redis/API connection strings (the `localhost`→IPv6 `::1` resolution intermittently fails), and the OIDC client now allows both host forms so sign-in works either way. Neither concern applies in production (real hostnames, no loopback).

## Runtime dependencies

- `AddInfrastructure` connects to RabbitMQ **synchronously at startup** (the audit outbox publisher). If RabbitMQ is down, the API host fails to start. Production should make this resilient (retry/backoff, or lazy connect) so a transient broker outage doesn't take down the API.
- Redis (data protection / session) and MariaDB are hard dependencies; production needs them highly available with health-gated startup.

## Not covered here

Observability (metrics/tracing/structured-log shipping), rate limiting, backup/restore runbooks, and horizontal-scaling specifics are out of scope for these notes.
