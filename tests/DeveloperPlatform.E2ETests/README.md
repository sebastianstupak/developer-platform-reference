# End-to-end tests (.NET + Playwright + Testcontainers)

Drives the Web UI with Playwright for .NET against a full stack that this project spins up itself
via Testcontainers (MariaDB, Redis, RabbitMQ, Keycloak) plus the API and Web apps as child processes.

## Requirements

- Docker Desktop running.
- No manual stack on ports 3306 / 5672 / 6379 / 8090 / 5274 / 5000 — stop `docker compose` first.

## Run

```bash
dotnet test tests/DeveloperPlatform.E2ETests
```

First run is slow (image pulls, Keycloak startup, Chromium install). The suite is tagged
`Category=E2E` and is excluded from the normal solution test run
(`dotnet test <slnx> --filter "Category!=E2E"`, which is what CI uses).
