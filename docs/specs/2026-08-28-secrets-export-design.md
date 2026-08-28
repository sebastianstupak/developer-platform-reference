# Machine Secret Export — Design

**Date:** 2026-08-28
**Status:** Approved (design)
**Feature:** A bulk "fetch all secrets for an environment" endpoint so applications/services can pull their config at startup with an API key, instead of revealing one secret at a time.

## 1. Goal

Complete the platform's core loop: humans **set** secrets in the UI, and applications **consume** them at runtime. Today the only value-read path is `POST …/secrets/{name}/reveal` (one secret per call, human-oriented). This adds a single call that returns all of an environment's secrets as a `name → value` JSON map, authenticated by an API key (or a user JWT) with `SecretsRead` scoped to that environment, decrypted via the existing per-tenant crypto, and audited as one event.

## 2. Approach

A new **audited command** (`ExportSecretsCommand`) + handler, exposed as `POST …/secrets/export`. Reuses the whole stack: the "Smart" auth scheme (JWT or `Bearer dpk_…` API key), the CQRS dispatch pipeline's permission + resource-scope enforcement, `ISecretRepository`, `ITenantCryptoService`, and the audit outbox. No new auth code.

**Verb:** `POST`, not `GET` — the audit outbox records **commands** only (queries bypass it), and a bulk secret read is exactly what should be audited. This matches the existing `POST …/{name}/reveal`, which is a command for the same reason.

## 3. Decisions (settled in brainstorming)

- **Response format:** JSON `name → value` map only (`{ "DATABASE_URL": "…" }`). No `.env` (clients can generate it; JSON avoids `.env` escaping edge cases).
- **Callers:** any principal — a service account via API key **or** a user via Keycloak JWT — with `SecretsRead` scoped to the environment. One policy, via the existing scheme.

## 4. Application layer

`src/DeveloperPlatform.Application/Secrets/ExportSecrets/ExportSecretsCommand.cs`:

```csharp
[RequiresPermission(Permission.SecretsRead)]
public record ExportSecretsCommand(Guid ProjectId, Guid EnvironmentId)
    : ICommand<ExportSecretsResult>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}

public record ExportSecretsResult(IReadOnlyDictionary<string, string> Secrets);
```

The command carries only ids (no secret value), so the audit `SensitiveDataScrubber` has nothing to redact. The result holds the decrypted values but results are never serialized to the audit payload (same as `RevealSecretResult`).

## 5. Infrastructure handler

`src/DeveloperPlatform.Infrastructure/Secrets/ExportSecretsCommandHandler.cs`:

```csharp
public sealed class ExportSecretsCommandHandler(
    ISecretRepository repository, ITenantCryptoService crypto, IExecutionContext ctx)
    : ICommandHandler<ExportSecretsCommand, ExportSecretsResult>
{
    public async Task<ExportSecretsResult> HandleAsync(ExportSecretsCommand command, CancellationToken ct = default)
    {
        var secrets = await repository.ListAsync(command.EnvironmentId, ct);   // tenant + env filtered, ordered by Name
        var map = new Dictionary<string, string>(secrets.Count);
        foreach (var s in secrets)
        {
            map[s.Name] = await crypto.DecryptAsync(ctx.TenantId, s.EncryptedValue, s.KeyId, ct);
        }
        return new ExportSecretsResult(map);
    }
}
```

- Each secret decrypts with **its own** `KeyId`, so export works across key rotations (retained keys), same guarantee as reveal-version.
- `ISecretRepository.ListAsync` already returns the env's `Secret` entities (with `EncryptedValue`/`KeyId`), tenant-filtered by the global query filter. No repository change needed.
- Decryption is a bounded loop over the env's secrets (config keys — tens, maybe low hundreds). No pagination (YAGNI).
- Registered in `ServiceCollectionExtensions.cs` beside the other secret handlers: `services.AddScoped<ICommandHandler<ExportSecretsCommand, ExportSecretsResult>, ExportSecretsCommandHandler>();`

## 6. API endpoint

Added to the existing secrets group in `SecretsEndpoints.cs`
(`/api/v1/projects/{projectId:guid}/environments/{environmentId:guid}/secrets`, already `.RequireAuthorization()`):

```csharp
group.MapPost("/export", async (Guid projectId, Guid environmentId, ICommandDispatcher d, CancellationToken ct) =>
{
    var result = await d.SendAsync<ExportSecretsCommand, ExportSecretsResult>(
        new ExportSecretsCommand(projectId, environmentId), ct);
    return Results.Ok(result.Secrets);
}).WithName("ExportSecrets").Produces<IReadOnlyDictionary<string, string>>();
```

- The group's `.RequireAuthorization()` + the "Smart" scheme mean an unauthenticated call gets `401`; a JWT or `Bearer dpk_…` API key both authenticate.
- `Results.Ok(result.Secrets)` serializes the dictionary directly to a JSON object `{ "NAME": "value", … }`. Empty environment → `{}`.

## 7. Authorization & audit

- **Authorization:** the dispatch pipeline reflects `[RequiresPermission(SecretsRead)]` off the command and resolves `IResourceScoped` → `Scope.Environment`, then calls the authorization service for the current principal (service account or user). Missing grant → `ForbiddenException` (`403`). A service account needs a `SecretsRead` grant scoped to the environment (or an ancestor project/tenant scope) — existing grant machinery, no change.
- **Audit:** `ExportSecretsCommand` flows through `CommandDispatcher`, producing **one** audit event (command type `ExportSecretsCommand`) with actor, environment scope, and IP. No secret values reach the audit payload. One record per export beats N reveal records.

## 8. Testing

- **Handler (Infrastructure/integration, InMemory + `TenantCryptoService`):**
  - Export returns every secret in the environment as `name → value` with correct plaintext.
  - Empty environment → empty map.
  - A secret encrypted under an **old, rotated key** still decrypts in the export (retained-key guarantee).
- **Authorization (dispatcher-level, `CommandDispatcher`):**
  - A **Member** principal: allowed with `SecretsRead` on the env; `ForbiddenException` without.
  - A **ServiceAccount** principal (the machine path): allowed with a `SecretsRead` env grant; `ForbiddenException` without — proving the API-key principal flows through the same scope check.
- **Endpoint (`WebApplicationFactory<Program>`):** `POST …/secrets/export` without auth → `401`.
- **Audit:** an authorized export writes an audit outbox entry of type `ExportSecretsCommand`.

## 9. Out of scope (YAGNI)

`.env` / other formats, pagination or size caps, a Web "Export / download" button (this feature is machine-facing; the button is a small follow-up if wanted), and any change to how API keys are issued or scoped.
