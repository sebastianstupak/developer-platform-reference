# Authorization Enforcement Implementation Plan (Authz Slice 3 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce permissions on every command/query via the dispatcher, resolve a real principal for each `/api` request, change `IExecutionContext`/audit from user/api-key to principal, and guard the existing Projects/API-key endpoints.

**Architecture:** A new `IAuthorizationService` resolves a principal's effective permissions (role assignments + direct grants, with scope cascade tenant ⊇ project ⊇ environment) against the Slice 2 model. A `[RequiresPermission(Permission.X)]` attribute on a command/query is enforced in `CommandDispatcher`/`QueryDispatcher` (same reflection pattern as `[SkipAudit]`), throwing `ForbiddenException` → HTTP 403. `ExecutionContextMiddleware` resolves the caller to a `PrincipalId` via a new `IPrincipalResolver` (JIT-creates the `User` + `Membership`; the **first member of a tenant becomes Owner** so existing flows keep working). The `IExecutionContext` and the whole audit chain swap `UserId`/`ApiKeyId` for `PrincipalId`/`PrincipalType` (keeping a nullable human `UserId`).

**Tech Stack:** .NET 10, EF Core 10 + MySQL, ASP.NET Core Minimal APIs + JWT bearer (Keycloak), xUnit + EF InMemory.

## Where this fits (authorization subsystem roadmap)

**Slice 3 of 6.** Spec: `docs/specs/2026-08-25-permissions-member-management-design.md` (§5 principal resolution, §6 enforcement). Depends on Slices 1 (catalog) & 2 (principal/grant model), both merged to `main`.

1. Permission catalog ✅  2. Principal & grant model ✅  3. **Enforcement** ← this plan  4. API-key auth + service accounts  5. Member invitations + onboarding + grant endpoints  6. Web Access UI

**Scope decision (user-approved): FULL WIRING.** This slice guards the existing Projects + API-key endpoints, requires a resolved principal on every `/api` request, and pulls a minimal onboarding path forward (JIT `User`/`Membership` + first-member-becomes-Owner). Deferred to later slices: the privilege-escalation guard (no grant commands exist until Slice 5), machine/service-account principal resolution (Slice 4 API-key auth), and the composite-`(PrincipalId, TenantId)` FK hardening (a follow-up).

## Global Constraints

- **Target framework** `net10.0`; `Nullable` + `ImplicitUsings` enabled. No type named `Action`.
- **Layering:** interfaces/attributes/exceptions in `DeveloperPlatform.Application`; implementations in `DeveloperPlatform.Infrastructure`; `Permission`/`Scope`/`PrincipalType` come from `DeveloperPlatform.Domain.Authorization`. Domain has no outward deps (enforced by `DomainLayerTests`).
- **Principal identity in audit:** the audit chain (`AuditOutboxEntry` → `AuditMessage` → `AuditEvent`) stores `PrincipalId (Guid?)` + `PrincipalType (string?)` + keeps `UserId (Guid?)`; the `ApiKeyId` column is removed.
- **Bootstrap policy:** the first `Membership` created in a tenant is assigned the **Owner** system role at tenant scope. Subsequent JIT members get an Active membership with **no** role (authenticated but unauthorized until granted/invited). *This is a reference-platform default — flag for review.*
- **Enforcement scope resolution order** (in the dispatcher): a command/query implementing `IResourceScoped` supplies its own `ResourceScope`; else the execution context's `EnvironmentId` → `Scope.Environment`, else `ProjectId` → `Scope.Project`, else `Scope.Tenant`.
- **Commits:** `commit-msg` lefthook hook REJECTS AI co-author trailers (no `Co-Authored-By:`/`Claude-Session:`). Conventional Commits. Pre-commit runs `dotnet build` + arch tests + `dotnet format` (~50s). `.gitattributes` mandates **CRLF** for `*.cs` — normalize new files (and strip any UTF-8 BOM on EF-generated files) or `dotnet format` rejects. `dotnet restore developer-platform-reference.slnx` if a build reports missing assets. Never `--no-verify`.
- **The migration step needs MySQL:** `docker compose up -d db`, wait healthy; run `dotnet ef` with `--project src/DeveloperPlatform.Infrastructure --startup-project src/DeveloperPlatform.Infrastructure`, and use a `127.0.0.1` connection string if `localhost` times out.
- **Test framework** xUnit; new tests under `tests/DeveloperPlatform.Api.Tests/Authorization/`.

---

## File Structure

**Created — Application:**
- `src/DeveloperPlatform.Application/Authorization/IAuthorizationService.cs`
- `src/DeveloperPlatform.Application/Authorization/ForbiddenException.cs`
- `src/DeveloperPlatform.Application/Authorization/IResourceScoped.cs`
- `src/DeveloperPlatform.Application/Authorization/IPrincipalResolver.cs`
- `src/DeveloperPlatform.Application/Attributes/RequiresPermissionAttribute.cs`

**Created — Infrastructure:**
- `src/DeveloperPlatform.Infrastructure/Authorization/AuthorizationService.cs`
- `src/DeveloperPlatform.Infrastructure/Authorization/PrincipalResolver.cs`
- `src/DeveloperPlatform.Infrastructure/Authorization/ForbiddenExceptionHandler.cs`

**Modified — Application:**
- `src/DeveloperPlatform.Application/Context/IExecutionContext.cs`
- `src/DeveloperPlatform.Application/Projects/CreateProject/CreateProjectCommand.cs`
- `src/DeveloperPlatform.Application/Projects/DeleteProject/DeleteProjectCommand.cs`
- `src/DeveloperPlatform.Application/Projects/GetProjects/GetProjectsQuery.cs`
- `src/DeveloperPlatform.Application/ApiKeys/CreateApiKey/CreateApiKeyCommand.cs`

**Modified — Domain:**
- `src/DeveloperPlatform.Domain/Audit/AuditOutboxEntry.cs`
- `src/DeveloperPlatform.Domain/Audit/AuditEvent.cs`

**Modified — Infrastructure:**
- `src/DeveloperPlatform.Infrastructure/Context/HttpExecutionContext.cs`
- `src/DeveloperPlatform.Infrastructure/Context/ExecutionContextMiddleware.cs`
- `src/DeveloperPlatform.Infrastructure/Dispatching/CommandDispatcher.cs`
- `src/DeveloperPlatform.Infrastructure/Dispatching/QueryDispatcher.cs`
- `src/DeveloperPlatform.Infrastructure/Messaging/AuditMessage.cs`
- `src/DeveloperPlatform.Infrastructure/Messaging/OutboxRelayWorker.cs`
- `src/DeveloperPlatform.Infrastructure/Messaging/AuditConsumer.cs`
- `src/DeveloperPlatform.Infrastructure/Persistence/Configurations/AuditOutboxEntryConfiguration.cs`
- `src/DeveloperPlatform.Infrastructure/Persistence/Configurations/AuditEventConfiguration.cs`
- `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs`
- `src/DeveloperPlatform.Api/Program.cs`
- (generated) `src/DeveloperPlatform.Infrastructure/Migrations/<ts>_AuditPrincipalColumns.cs`

**Modified — Tests:**
- `tests/DeveloperPlatform.Api.Tests/Crypto/TenantCryptoServiceTests.cs`
- `tests/DeveloperPlatform.Api.Tests/Authorization/AuthorizationPersistenceTests.cs`
- `tests/DeveloperPlatform.Api.Tests/Dispatching/CommandDispatcherTests.cs`
- `tests/DeveloperPlatform.Api.Tests/Context/ExecutionContextMiddlewareTests.cs`

**Tests (new):**
- `tests/DeveloperPlatform.Api.Tests/Authorization/AuthorizationServiceTests.cs`
- `tests/DeveloperPlatform.Api.Tests/Authorization/EnforcementTests.cs`

---

## Task 1: IAuthorizationService (effective-permission resolution)

Build the resolution engine first — it depends only on the Slice 2 model and is fully unit-testable.

**Files:**
- Create: `src/DeveloperPlatform.Application/Authorization/IAuthorizationService.cs`, `ForbiddenException.cs`
- Create: `src/DeveloperPlatform.Infrastructure/Authorization/AuthorizationService.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Authorization/AuthorizationServiceTests.cs`

**Interfaces:**
- Consumes: `Permission`, `Scope`, `ScopeType` (Domain); `ApplicationDbContext`, `PermissionGrant`, `RoleAssignment`, `RolePermission`, `ProjectEnvironment` (Slice 2 / existing).
- Produces:
  - `interface IAuthorizationService { Task<bool> IsAuthorizedAsync(Guid principalId, Permission permission, Scope scope, CancellationToken ct = default); Task AuthorizeAsync(Guid principalId, Permission permission, Scope scope, CancellationToken ct = default); }`
  - `class ForbiddenException : Exception`

- [ ] **Step 1: Write the failing tests**

Create `tests/DeveloperPlatform.Api.Tests/Authorization/AuthorizationServiceTests.cs`:

```csharp
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Projects;
using DeveloperPlatform.Infrastructure.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class AuthorizationServiceTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private AuthorizationService _sut = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _principal = Guid.NewGuid();
    private readonly Guid _project = Guid.NewGuid();
    private readonly Guid _env = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var ctx = new TestExecutionContext { TenantId = _tenant };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
        // an environment under the project (for cascade resolution)
        _db.ProjectEnvironments.Add(ProjectEnvironment.Create(_tenant, _project, "prod", EnvironmentType.Production));
        // give THIS env a known id by re-fetching is unnecessary; instead add a grant keyed to a real env id:
        await _db.SaveChangesAsync();
        _sut = new AuthorizationService(_db);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Direct_Grant_At_Tenant_Scope_Allows()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsRead, Scope.Tenant));
        await _db.SaveChangesAsync();

        Assert.True(await _sut.IsAuthorizedAsync(_principal, Permission.SecretsRead, Scope.Tenant));
        // tenant grant cascades down to a project-scoped request
        Assert.True(await _sut.IsAuthorizedAsync(_principal, Permission.SecretsRead, Scope.Project(_project)));
    }

    [Fact]
    public async Task Project_Grant_Does_Not_Satisfy_Other_Project()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.ProjectsWrite, Scope.Project(_project)));
        await _db.SaveChangesAsync();

        Assert.True(await _sut.IsAuthorizedAsync(_principal, Permission.ProjectsWrite, Scope.Project(_project)));
        Assert.False(await _sut.IsAuthorizedAsync(_principal, Permission.ProjectsWrite, Scope.Project(Guid.NewGuid())));
        Assert.False(await _sut.IsAuthorizedAsync(_principal, Permission.ProjectsWrite, Scope.Tenant));
    }

    [Fact]
    public async Task Project_Grant_Cascades_To_Its_Environment()
    {
        var envEntity = ProjectEnvironment.Create(_tenant, _project, "staging", EnvironmentType.Staging);
        _db.ProjectEnvironments.Add(envEntity);
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsWrite, Scope.Project(_project)));
        await _db.SaveChangesAsync();

        Assert.True(await _sut.IsAuthorizedAsync(_principal, Permission.SecretsWrite, Scope.Environment(envEntity.Id)));
    }

    [Fact]
    public async Task Role_Assignment_Grants_Its_Permissions()
    {
        var roleId = Guid.NewGuid();
        _db.Roles.Add(Role.CreateSystem(roleId, "TestRole", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        _db.RolePermissions.Add(RolePermission.Create(roleId, Permission.AuditRead));
        _db.RoleAssignments.Add(RoleAssignment.Create(_tenant, _principal, roleId, Scope.Tenant));
        await _db.SaveChangesAsync();

        Assert.True(await _sut.IsAuthorizedAsync(_principal, Permission.AuditRead, Scope.Tenant));
        Assert.False(await _sut.IsAuthorizedAsync(_principal, Permission.SecretsWrite, Scope.Tenant));
    }

    [Fact]
    public async Task Unknown_Principal_Is_Denied_And_Authorize_Throws()
    {
        Assert.False(await _sut.IsAuthorizedAsync(Guid.NewGuid(), Permission.ProjectsRead, Scope.Tenant));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.AuthorizeAsync(Guid.NewGuid(), Permission.ProjectsRead, Scope.Tenant));
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        public Guid TenantId { get; set; }
        public Guid? PrincipalId => null;
        public DeveloperPlatform.Domain.Authorization.PrincipalType? PrincipalType => null;
        public Guid? UserId => null;
        public Guid? ProjectId => null;
        public Guid? EnvironmentId => null;
        public string IpAddress => "127.0.0.1";
        public bool IsCrossTenantOperation { get; set; }
    }
}
```

Note: this test uses the NEW `IExecutionContext` shape (`PrincipalId`/`PrincipalType`). That interface change lands in Task 2 — so this test will not compile until Task 2. **Sequencing:** implement Task 1's production code now, but its test's `TestExecutionContext` depends on the Task 2 interface. To keep Task 1 self-contained, use the CURRENT interface shape in this test's `TestExecutionContext` (i.e. `UserId`/`ApiKeyId`, no `PrincipalId`) for Task 1, and update it in Task 2 along with the others. Replace the `TestExecutionContext` above with the current-shape version:

```csharp
    private sealed class TestExecutionContext : IExecutionContext
    {
        public Guid TenantId { get; set; }
        public Guid? UserId => null;
        public Guid? ApiKeyId => null;
        public Guid? ProjectId => null;
        public Guid? EnvironmentId => null;
        public string IpAddress => "127.0.0.1";
        public bool IsCrossTenantOperation { get; set; }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~AuthorizationServiceTests"`
Expected: FAIL — `IAuthorizationService`/`AuthorizationService`/`ForbiddenException` do not exist.

- [ ] **Step 3: Create the Application contracts**

`src/DeveloperPlatform.Application/Authorization/ForbiddenException.cs`:

```csharp
namespace DeveloperPlatform.Application.Authorization;

// Thrown when a principal lacks a required permission. Mapped to HTTP 403 by the API.
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}
```

`src/DeveloperPlatform.Application/Authorization/IAuthorizationService.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Authorization;

public interface IAuthorizationService
{
    Task<bool> IsAuthorizedAsync(Guid principalId, Permission permission, Scope scope, CancellationToken ct = default);

    // Throws ForbiddenException when the principal is not authorized.
    Task AuthorizeAsync(Guid principalId, Permission permission, Scope scope, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement `AuthorizationService`**

`src/DeveloperPlatform.Infrastructure/Authorization/AuthorizationService.cs`:

```csharp
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Authorization;

// Resolves whether a principal holds `permission` at `scope`, honouring the scope hierarchy
// (tenant ⊇ project ⊇ environment) across both direct grants and role assignments.
public sealed class AuthorizationService(ApplicationDbContext db) : IAuthorizationService
{
    public async Task AuthorizeAsync(Guid principalId, Permission permission, Scope scope, CancellationToken ct = default)
    {
        if (!await IsAuthorizedAsync(principalId, permission, scope, ct))
        {
            throw new ForbiddenException(
                $"Principal {principalId} lacks permission '{PermissionCatalog.ToToken(permission)}' at {scope.Type}.");
        }
    }

    public async Task<bool> IsAuthorizedAsync(
        Guid principalId, Permission permission, Scope scope, CancellationToken ct = default)
    {
        var ancestors = await AncestorScopesAsync(scope, ct);

        // Direct permission grants (tenant filter auto-applies to the current tenant).
        var grants = await db.PermissionGrants
            .Where(g => g.PrincipalId == principalId && g.Permission == permission)
            .ToListAsync(ct);
        if (grants.Any(g => ancestors.Contains(g.Scope)))
        {
            return true;
        }

        // Role assignments whose scope covers the request, expanded to their permissions.
        var assignments = await db.RoleAssignments
            .Where(a => a.PrincipalId == principalId)
            .ToListAsync(ct);
        var roleIds = assignments
            .Where(a => ancestors.Contains(a.Scope))
            .Select(a => a.RoleId)
            .Distinct()
            .ToList();
        if (roleIds.Count == 0)
        {
            return false;
        }

        return await db.RolePermissions
            .AnyAsync(rp => roleIds.Contains(rp.RoleId) && rp.Permission == permission, ct);
    }

    // The set of scopes that "cover" the requested scope: itself plus its ancestors.
    // Environment → its parent project (looked up) → tenant. Project → tenant. Tenant → itself.
    private async Task<HashSet<Scope>> AncestorScopesAsync(Scope scope, CancellationToken ct)
    {
        var set = new HashSet<Scope> { Scope.Tenant };
        switch (scope.Type)
        {
            case ScopeType.Project:
                set.Add(scope);
                break;
            case ScopeType.Environment:
                set.Add(scope);
                var projectId = await db.ProjectEnvironments
                    .Where(e => e.Id == scope.TargetId)
                    .Select(e => (Guid?)e.ProjectId)
                    .FirstOrDefaultAsync(ct);
                if (projectId is Guid pid)
                {
                    set.Add(Scope.Project(pid));
                }
                break;
            case ScopeType.Tenant:
            default:
                break;
        }
        return set;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~AuthorizationServiceTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add src/DeveloperPlatform.Application/Authorization src/DeveloperPlatform.Infrastructure/Authorization/AuthorizationService.cs tests/DeveloperPlatform.Api.Tests/Authorization/AuthorizationServiceTests.cs
git commit -m "feat(authz): IAuthorizationService with role+grant scope-cascade resolution"
```

---

## Task 2: Breaking IExecutionContext + audit pipeline (principal identity)

Swap `UserId`/`ApiKeyId` for `PrincipalId`/`PrincipalType` across the context and the entire audit chain, keeping a nullable human `UserId`. Mechanical but wide; the gate is "solution builds + existing dispatcher/crypto/persistence tests pass + audit migration generates."

**Files:** (Modify) `IExecutionContext.cs`, `HttpExecutionContext.cs`, `ExecutionContextMiddleware.cs`, `AuditOutboxEntry.cs`, `AuditEvent.cs`, `CommandDispatcher.cs`, `AuditMessage.cs`, `OutboxRelayWorker.cs`, `AuditConsumer.cs`, `AuditOutboxEntryConfiguration.cs`, `AuditEventConfiguration.cs`; the three test `TestExecutionContext`/`HttpExecutionContext` usages; (Generated) audit migration.

**Interfaces:**
- Consumes: `PrincipalType` (Domain).
- Produces: `IExecutionContext` with `Guid? PrincipalId`, `PrincipalType? PrincipalType`, `Guid? UserId` (no `ApiKeyId`). `AuditOutboxEntry.Create(...)` / `AuditEvent.Create(...)` / `AuditMessage` take `principalId`/`principalType`/`userId` (no `apiKeyId`).

- [ ] **Step 1: Change `IExecutionContext`**

`src/DeveloperPlatform.Application/Context/IExecutionContext.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Context;

public interface IExecutionContext
{
    Guid TenantId { get; }
    Guid? PrincipalId { get; }
    PrincipalType? PrincipalType { get; }
    Guid? UserId { get; }          // the human behind a Member principal; null for service accounts
    Guid? ProjectId { get; }
    Guid? EnvironmentId { get; }
    string IpAddress { get; }
    bool IsCrossTenantOperation { get; set; }
}
```

- [ ] **Step 2: Update `HttpExecutionContext`**

`src/DeveloperPlatform.Infrastructure/Context/HttpExecutionContext.cs`:

```csharp
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Infrastructure.Context;

public sealed class HttpExecutionContext : IExecutionContext
{
    public Guid TenantId { get; internal set; }
    public Guid? PrincipalId { get; internal set; }
    public PrincipalType? PrincipalType { get; internal set; }
    public Guid? UserId { get; internal set; }
    public Guid? ProjectId { get; internal set; }
    public Guid? EnvironmentId { get; internal set; }
    public string IpAddress { get; internal set; } = string.Empty;
    public bool IsCrossTenantOperation { get; set; }
}
```

- [ ] **Step 3: Simplify `ExecutionContextMiddleware` for now (principal wiring lands in Task 4)**

For this task, keep the middleware compiling by removing the old `UserId`/`ApiKeyId` claim reads. Task 4 replaces the body with real principal resolution. Set `src/DeveloperPlatform.Infrastructure/Context/ExecutionContextMiddleware.cs` to:

```csharp
using Microsoft.AspNetCore.Http;

namespace DeveloperPlatform.Infrastructure.Context;

public sealed class ExecutionContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, HttpExecutionContext executionContext)
    {
        var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value
            ?? throw new UnauthorizedAccessException("tenant_id claim is required.");

        if (!Guid.TryParse(tenantClaim, out var tenantId))
        {
            throw new UnauthorizedAccessException("tenant_id claim is not a valid GUID.");
        }

        executionContext.TenantId = tenantId;
        executionContext.IpAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (Guid.TryParse(httpContext.User.FindFirst("project_id")?.Value, out var projectId))
        {
            executionContext.ProjectId = projectId;
        }

        if (Guid.TryParse(httpContext.User.FindFirst("environment_id")?.Value, out var envId))
        {
            executionContext.EnvironmentId = envId;
        }

        await next(httpContext);
    }
}
```

- [ ] **Step 4: Update the audit domain entities**

In `src/DeveloperPlatform.Domain/Audit/AuditOutboxEntry.cs`: replace the `ApiKeyId` property with two properties and update `Create`. Change the property block (lines with `UserId`/`ApiKeyId`) to:

```csharp
    public Guid? PrincipalId { get; private set; }
    public string? PrincipalType { get; private set; }
    public Guid? UserId { get; private set; }
```

and change the `Create` signature + body from `Guid? userId, Guid? apiKeyId, ...` to:

```csharp
    public static AuditOutboxEntry Create(
        Guid tenantId, string commandType, AuditStatus status,
        Guid? principalId, string? principalType, Guid? userId,
        Guid? projectId, Guid? environmentId,
        string ipAddress, bool isCrossTenant, string? crossTenantReason,
        byte[] encryptedPayload, Guid keyId)
    {
        return new AuditOutboxEntry
        {
            TenantId = tenantId,
            CommandType = commandType,
            Status = status,
            PrincipalId = principalId,
            PrincipalType = principalType,
            UserId = userId,
            ProjectId = projectId,
            EnvironmentId = environmentId,
            IpAddress = ipAddress,
            IsCrossTenant = isCrossTenant,
            CrossTenantReason = crossTenantReason,
            EncryptedPayload = encryptedPayload,
            KeyId = keyId
        };
    }
```

Apply the identical change to `src/DeveloperPlatform.Domain/Audit/AuditEvent.cs`: replace `UserId`/`ApiKeyId` properties with `PrincipalId`/`PrincipalType`/`UserId` (as above), update `Create` the same way, and update `FromOutboxEntry` to map `PrincipalId = entry.PrincipalId, PrincipalType = entry.PrincipalType, UserId = entry.UserId` (remove the `ApiKeyId` line).

(`PrincipalType` is stored as a string in audit rows to keep the audit record self-contained and provider-agnostic; the acting principal's type is `executionContext.PrincipalType?.ToString()`.)

- [ ] **Step 5: Update the audit configs**

`AuditOutboxEntryConfiguration.cs` and `AuditEventConfiguration.cs` need no property removals (EF maps by convention), but add a max length for the new string column. Add to BOTH `Configure` methods:

```csharp
        builder.Property(e => e.PrincipalType).HasMaxLength(20);
```

- [ ] **Step 6: Update the dispatcher, message record, relay, and consumer**

In `src/DeveloperPlatform.Infrastructure/Dispatching/CommandDispatcher.cs`, in `BuildOutboxEntryAsync`, change the `AuditOutboxEntry.Create(...)` call's identity args from:

```csharp
            userId: executionContext.UserId,
            apiKeyId: executionContext.ApiKeyId,
```

to:

```csharp
            principalId: executionContext.PrincipalId,
            principalType: executionContext.PrincipalType?.ToString(),
            userId: executionContext.UserId,
```

In `src/DeveloperPlatform.Infrastructure/Messaging/AuditMessage.cs`, replace `Guid? UserId, Guid? ApiKeyId,` with:

```csharp
    Guid? PrincipalId,
    string? PrincipalType,
    Guid? UserId,
```

In `src/DeveloperPlatform.Infrastructure/Messaging/OutboxRelayWorker.cs`, change `ToMessage` identity args from `entry.UserId, entry.ApiKeyId,` to `entry.PrincipalId, entry.PrincipalType, entry.UserId,`.

In `src/DeveloperPlatform.Infrastructure/Messaging/AuditConsumer.cs`, change the `AuditEvent.Create(...)` identity args from `message.UserId, message.ApiKeyId,` to `message.PrincipalId, message.PrincipalType, message.UserId,`.

- [ ] **Step 7: Update the three test execution-contexts**

In `tests/DeveloperPlatform.Api.Tests/Crypto/TenantCryptoServiceTests.cs` and `tests/DeveloperPlatform.Api.Tests/Authorization/AuthorizationPersistenceTests.cs` and `tests/DeveloperPlatform.Api.Tests/Authorization/AuthorizationServiceTests.cs`, replace the `TestExecutionContext` members `Guid? UserId => null; Guid? ApiKeyId => null;` with:

```csharp
        public Guid? PrincipalId => null;
        public DeveloperPlatform.Domain.Authorization.PrincipalType? PrincipalType => null;
        public Guid? UserId => null;
```

`CommandDispatcherTests.cs` constructs `new HttpExecutionContext { TenantId = _tenantId, IpAddress = "127.0.0.1" }` — that still compiles (the removed properties weren't set). No change needed there for the context itself.

- [ ] **Step 8: Build the solution**

Run: `dotnet build developer-platform-reference.slnx --no-restore`
Expected: `Build succeeded. 0 Error(s)`. Fix any remaining reference to `.ApiKeyId`/old `Create` signatures.

- [ ] **Step 9: Run the affected tests**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Dispatching|FullyQualifiedName~Crypto|FullyQualifiedName~AuthorizationServiceTests|FullyQualifiedName~AuthorizationPersistenceTests"`
Expected: PASS (all of them). The `CommandDispatcherTests` audit assertions still pass — the outbox entry is written; only its identity columns changed.

- [ ] **Step 10: Generate the audit migration**

Ensure `docker compose up -d db` is healthy. Then:
Run: `dotnet ef migrations add AuditPrincipalColumns --project src/DeveloperPlatform.Infrastructure --startup-project src/DeveloperPlatform.Infrastructure`
Expected: `Done.` Verify it drops `ApiKeyId` and adds `PrincipalId`/`PrincipalType` on both `AuditOutboxEntries` and `AuditEvents`:
Run: `grep -oE '(DropColumn|AddColumn)[^;]*"(ApiKeyId|PrincipalId|PrincipalType)"' src/DeveloperPlatform.Infrastructure/Migrations/*_AuditPrincipalColumns.cs`
Expected: DropColumn ApiKeyId (x2), AddColumn PrincipalId + PrincipalType (x2).

- [ ] **Step 11: Build again, then commit**

Run: `dotnet build developer-platform-reference.slnx --no-restore` → 0 errors. (Strip any BOM from the generated migration if `dotnet format` complains.)

```bash
git add -A
git commit -m "feat(authz): principal identity in execution context and audit chain"
```

---

## Task 3: [RequiresPermission] enforcement in the dispatcher + 403/401 mapping

**Files:**
- Create: `src/DeveloperPlatform.Application/Attributes/RequiresPermissionAttribute.cs`, `src/DeveloperPlatform.Application/Authorization/IResourceScoped.cs`
- Create: `src/DeveloperPlatform.Infrastructure/Authorization/ForbiddenExceptionHandler.cs`
- Modify: `CommandDispatcher.cs`, `QueryDispatcher.cs`, `ServiceCollectionExtensions.cs`, `Program.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Authorization/EnforcementTests.cs`

**Interfaces:**
- Consumes: `IAuthorizationService`, `ForbiddenException` (Task 1); `IExecutionContext` (Task 2); `Permission`/`Scope` (Domain).
- Produces: `RequiresPermissionAttribute(Permission)`, `IResourceScoped { Scope ResourceScope { get; } }`, and dispatcher enforcement.

- [ ] **Step 1: Write the failing enforcement tests**

Create `tests/DeveloperPlatform.Api.Tests/Authorization/EnforcementTests.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Audit;
using DeveloperPlatform.Infrastructure.Context;
using DeveloperPlatform.Infrastructure.Crypto;
using DeveloperPlatform.Infrastructure.Dispatching;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class EnforcementTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private HttpExecutionContext _ctx = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _principal = Guid.NewGuid();
    private static readonly byte[] Key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    public async Task InitializeAsync()
    {
        _ctx = new HttpExecutionContext { TenantId = _tenant, IpAddress = "127.0.0.1", PrincipalId = _principal, PrincipalType = PrincipalType.Member };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        _db = new ApplicationDbContext(options, _ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private CommandDispatcher Build()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<GuardedCommand, Unit>, GuardedHandler>();
        var sp = services.BuildServiceProvider();
        var authz = new DeveloperPlatform.Infrastructure.Authorization.AuthorizationService(_db);
        return new CommandDispatcher(sp, _db, _ctx, new TenantCryptoService(_db, Key),
            new AuditOutboxRepository(_db), new SensitiveDataScrubber(), TenancyMode.SharedTables, authz);
    }

    [Fact]
    public async Task Guarded_Command_Throws_Forbidden_Without_Permission()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => Build().SendAsync<GuardedCommand, Unit>(new GuardedCommand()));
    }

    [Fact]
    public async Task Guarded_Command_Succeeds_With_Grant()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsWrite, Scope.Tenant));
        await _db.SaveChangesAsync();

        var result = await Build().SendAsync<GuardedCommand, Unit>(new GuardedCommand());
        Assert.Equal(Unit.Value, result);
    }

    [RequiresPermission(Permission.SecretsWrite)]
    public record GuardedCommand : ICommand;

    public class GuardedHandler : ICommandHandler<GuardedCommand, Unit>
    {
        public Task<Unit> HandleAsync(GuardedCommand command, CancellationToken ct = default)
            => Task.FromResult(Unit.Value);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~EnforcementTests"`
Expected: FAIL — `RequiresPermissionAttribute` does not exist / `CommandDispatcher` has no `authz` parameter.

- [ ] **Step 3: Create the attribute and interface**

`src/DeveloperPlatform.Application/Attributes/RequiresPermissionAttribute.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class RequiresPermissionAttribute(Permission permission) : Attribute
{
    public Permission Permission { get; } = permission;
}
```

`src/DeveloperPlatform.Application/Authorization/IResourceScoped.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Authorization;

// A command/query implements this to declare the specific resource scope it acts on,
// so per-instance ACLs (e.g. "write on project X") are enforced.
public interface IResourceScoped
{
    Scope ResourceScope { get; }
}
```

- [ ] **Step 4: Enforce in `CommandDispatcher`**

In `src/DeveloperPlatform.Infrastructure/Dispatching/CommandDispatcher.cs`: add `IAuthorizationService authorizationService` as the LAST primary-constructor parameter (after `TenancyMode tenancyMode`), add `using DeveloperPlatform.Application.Authorization;` and `using DeveloperPlatform.Domain.Authorization;`, and insert an enforcement block at the start of `SendAsync`, before `BeginTransactionAsync`:

```csharp
        var requiresPermission = typeof(TCommand).GetCustomAttribute<RequiresPermissionAttribute>();
        if (requiresPermission is not null)
        {
            if (executionContext.PrincipalId is not Guid principalId)
            {
                throw new ForbiddenException("No principal in the execution context.");
            }

            var scope = command is IResourceScoped scoped
                ? scoped.ResourceScope
                : executionContext.EnvironmentId is Guid envId
                    ? Scope.Environment(envId)
                    : executionContext.ProjectId is Guid projId
                        ? Scope.Project(projId)
                        : Scope.Tenant;

            await authorizationService.AuthorizeAsync(principalId, requiresPermission.Permission, scope, ct);
        }
```

- [ ] **Step 5: Enforce in `QueryDispatcher`**

Rewrite `src/DeveloperPlatform.Infrastructure/Dispatching/QueryDispatcher.cs` to include the same enforcement (queries can be read-guarded, e.g. `projects:read`):

```csharp
using System.Reflection;
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Infrastructure.Dispatching;

public sealed class QueryDispatcher(
    IServiceProvider serviceProvider,
    IExecutionContext executionContext,
    IAuthorizationService authorizationService) : IQueryDispatcher
{
    public async Task<TResult> SendAsync<TQuery, TResult>(TQuery query, CancellationToken ct = default)
        where TQuery : IQuery<TResult>
    {
        var requiresPermission = typeof(TQuery).GetCustomAttribute<RequiresPermissionAttribute>();
        if (requiresPermission is not null)
        {
            if (executionContext.PrincipalId is not Guid principalId)
            {
                throw new ForbiddenException("No principal in the execution context.");
            }

            var scope = query is IResourceScoped scoped
                ? scoped.ResourceScope
                : executionContext.EnvironmentId is Guid envId
                    ? Scope.Environment(envId)
                    : executionContext.ProjectId is Guid projId
                        ? Scope.Project(projId)
                        : Scope.Tenant;

            await authorizationService.AuthorizeAsync(principalId, requiresPermission.Permission, scope, ct);
        }

        var handler = serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>();
        return await handler.HandleAsync(query, ct);
    }
}
```

- [ ] **Step 6: Register the service + exception handler**

In `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs`, add `using DeveloperPlatform.Application.Authorization;` and `using DeveloperPlatform.Infrastructure.Authorization;`, and register (near the other `AddScoped` calls, before the dispatchers):

```csharp
        services.AddScoped<IAuthorizationService, AuthorizationService>();
```

`src/DeveloperPlatform.Infrastructure/Authorization/ForbiddenExceptionHandler.cs`:

```csharp
using DeveloperPlatform.Application.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace DeveloperPlatform.Infrastructure.Authorization;

// Maps authorization failures to RFC problem responses: ForbiddenException → 403,
// UnauthorizedAccessException (e.g. missing tenant claim) → 401.
public sealed class ForbiddenExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var status = exception switch
        {
            ForbiddenException => StatusCodes.Status403Forbidden,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            _ => 0
        };
        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc9110",
            title = status == 403 ? "Forbidden" : "Unauthorized",
            status
        }, ct);
        return true;
    }
}
```

In `src/DeveloperPlatform.Api/Program.cs`, register the handler with the other service registrations (after `builder.Services.AddProblemDetails();`):

```csharp
    builder.Services.AddExceptionHandler<DeveloperPlatform.Infrastructure.Authorization.ForbiddenExceptionHandler>();
```

(`app.UseExceptionHandler()` is already called, so the handler is invoked.)

- [ ] **Step 7: Update `CommandDispatcherTests` construction**

`CommandDispatcherTests` constructs `new CommandDispatcher(sp, _db, ctx, crypto, repo, scrubber, TenancyMode.SharedTables)` in two places. Add a final argument `new DeveloperPlatform.Infrastructure.Authorization.AuthorizationService(_db)` (and the matching `db` variant in the DatabasePerTenant test). The existing test commands have no `[RequiresPermission]`, so enforcement is skipped and the audit assertions are unaffected.

- [ ] **Step 8: Run enforcement + dispatcher tests**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~EnforcementTests|FullyQualifiedName~Dispatching"`
Expected: PASS. Then `dotnet build developer-platform-reference.slnx --no-restore` → 0 errors.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(authz): [RequiresPermission] dispatcher enforcement with 403/401 mapping"
```

---

## Task 4: Principal resolution in the middleware (JIT user + first-member-Owner)

**Files:**
- Create: `src/DeveloperPlatform.Application/Authorization/IPrincipalResolver.cs`, `src/DeveloperPlatform.Infrastructure/Authorization/PrincipalResolver.cs`
- Modify: `ExecutionContextMiddleware.cs`, `ServiceCollectionExtensions.cs`, `ExecutionContextMiddlewareTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`, `User`, `Principal`, `Membership`, `RoleAssignment`, `SystemRoles` (Slice 2); `PrincipalType`, `Scope` (Domain).
- Produces: `interface IPrincipalResolver { Task<ResolvedPrincipal?> ResolveAsync(ClaimsPrincipal user, Guid tenantId, CancellationToken ct); }` and `record ResolvedPrincipal(Guid PrincipalId, PrincipalType Type, Guid? UserId)`.

- [ ] **Step 1: Create the resolver contract**

`src/DeveloperPlatform.Application/Authorization/IPrincipalResolver.cs`:

```csharp
using System.Security.Claims;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Authorization;

public sealed record ResolvedPrincipal(Guid PrincipalId, PrincipalType Type, Guid? UserId);

public interface IPrincipalResolver
{
    // Resolves the authenticated caller to a principal in `tenantId`, JIT-creating the User/Membership
    // on first login. Returns null if the token carries no usable subject.
    Task<ResolvedPrincipal?> ResolveAsync(ClaimsPrincipal user, Guid tenantId, CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement `PrincipalResolver`**

`src/DeveloperPlatform.Infrastructure/Authorization/PrincipalResolver.cs`:

```csharp
using System.Security.Claims;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Authorization;

public sealed class PrincipalResolver(ApplicationDbContext db) : IPrincipalResolver
{
    public async Task<ResolvedPrincipal?> ResolveAsync(
        ClaimsPrincipal user, Guid tenantId, CancellationToken ct = default)
    {
        var subject = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        // Find-or-JIT-create the global User (keyed by Keycloak subject).
        var dbUser = await db.Users.FirstOrDefaultAsync(u => u.KeycloakSubject == subject, ct);
        if (dbUser is null)
        {
            var email = user.FindFirst("email")?.Value ?? $"{subject}@unknown";
            var displayName = user.FindFirst("preferred_username")?.Value
                ?? user.FindFirst("name")?.Value ?? email;
            dbUser = User.Create(subject, email, displayName);
            db.Users.Add(dbUser);
            await db.SaveChangesAsync(ct);
        }

        // Find the membership for this user in this tenant.
        var membership = await db.Memberships
            .FirstOrDefaultAsync(m => m.UserId == dbUser.Id, ct); // tenant filter scopes to current tenant
        if (membership is not null)
        {
            return new ResolvedPrincipal(membership.PrincipalId, PrincipalType.Member, dbUser.Id);
        }

        // JIT-create a principal + membership. Bootstrap: the first member of a tenant becomes Owner.
        var isFirstMember = !await db.Memberships.AnyAsync(ct); // filtered to this tenant
        var principal = Principal.CreateMember(tenantId, dbUser.DisplayName);
        db.Principals.Add(principal);
        db.Memberships.Add(Membership.Create(tenantId, principal.Id, dbUser.Id, MembershipStatus.Active));
        if (isFirstMember)
        {
            db.RoleAssignments.Add(RoleAssignment.Create(tenantId, principal.Id, SystemRoles.OwnerId, Scope.Tenant));
        }
        await db.SaveChangesAsync(ct);

        return new ResolvedPrincipal(principal.Id, PrincipalType.Member, dbUser.Id);
    }
}
```

- [ ] **Step 3: Wire the resolver into the middleware**

Update `src/DeveloperPlatform.Infrastructure/Context/ExecutionContextMiddleware.cs` to resolve the principal after setting the tenant. Replace the file with:

```csharp
using DeveloperPlatform.Application.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Infrastructure.Context;

public sealed class ExecutionContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, HttpExecutionContext executionContext)
    {
        var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value
            ?? throw new UnauthorizedAccessException("tenant_id claim is required.");

        if (!Guid.TryParse(tenantClaim, out var tenantId))
        {
            throw new UnauthorizedAccessException("tenant_id claim is not a valid GUID.");
        }

        executionContext.TenantId = tenantId;
        executionContext.IpAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (Guid.TryParse(httpContext.User.FindFirst("project_id")?.Value, out var projectId))
        {
            executionContext.ProjectId = projectId;
        }

        if (Guid.TryParse(httpContext.User.FindFirst("environment_id")?.Value, out var envId))
        {
            executionContext.EnvironmentId = envId;
        }

        var resolver = httpContext.RequestServices.GetRequiredService<IPrincipalResolver>();
        var resolved = await resolver.ResolveAsync(httpContext.User, tenantId, httpContext.RequestAborted);
        if (resolved is not null)
        {
            executionContext.PrincipalId = resolved.PrincipalId;
            executionContext.PrincipalType = resolved.Type;
            executionContext.UserId = resolved.UserId;
        }

        await next(httpContext);
    }
}
```

- [ ] **Step 4: Register the resolver**

In `ServiceCollectionExtensions.cs`, add near the authorization registration:

```csharp
        services.AddScoped<IPrincipalResolver, PrincipalResolver>();
```

- [ ] **Step 5: Update `ExecutionContextMiddlewareTests`**

The middleware now resolves `IPrincipalResolver` from `RequestServices`. Update `tests/DeveloperPlatform.Api.Tests/Context/ExecutionContextMiddlewareTests.cs`: extend `FakeServiceProvider` to also return a stub resolver, and assert the principal is populated. Replace the file with:

```csharp
using System.Security.Claims;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Context;
using Microsoft.AspNetCore.Http;

namespace DeveloperPlatform.Api.Tests.Context;

public class ExecutionContextMiddlewareTests
{
    [Fact]
    public async Task Middleware_Populates_Tenant_And_Principal()
    {
        var tenantId = Guid.NewGuid();
        var principalId = Guid.NewGuid();
        var ctx = new HttpExecutionContext();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("sub", Guid.NewGuid().ToString())
        ]));
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        httpContext.RequestServices = new FakeServiceProvider(ctx,
            new StubResolver(new ResolvedPrincipal(principalId, PrincipalType.Member, Guid.NewGuid())));

        var middleware = new ExecutionContextMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(httpContext, ctx);

        Assert.Equal(tenantId, ctx.TenantId);
        Assert.Equal(principalId, ctx.PrincipalId);
        Assert.Equal(PrincipalType.Member, ctx.PrincipalType);
    }

    [Fact]
    public async Task Middleware_Throws_When_TenantId_Claim_Missing()
    {
        var ctx = new HttpExecutionContext();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", Guid.NewGuid().ToString())
        ]));

        var middleware = new ExecutionContextMiddleware(_ => Task.CompletedTask);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => middleware.InvokeAsync(httpContext, ctx));
    }

    private sealed class StubResolver(ResolvedPrincipal? result) : IPrincipalResolver
    {
        public Task<ResolvedPrincipal?> ResolveAsync(ClaimsPrincipal user, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult(result);
    }

    private sealed class FakeServiceProvider(HttpExecutionContext ctx, IPrincipalResolver resolver) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(HttpExecutionContext) ? ctx
            : serviceType == typeof(IPrincipalResolver) ? resolver
            : null;
    }
}
```

- [ ] **Step 6: Run the middleware tests + build**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~ExecutionContextMiddlewareTests"`
Expected: PASS (2 tests). Then `dotnet build developer-platform-reference.slnx --no-restore` → 0 errors.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(authz): JIT principal resolution in middleware (first member becomes Owner)"
```

---

## Task 5: Guard the existing Projects & API-key endpoints

**Files:**
- Modify: `CreateProjectCommand.cs`, `DeleteProjectCommand.cs`, `GetProjectsQuery.cs`, `CreateApiKeyCommand.cs`
- (verify) no new test needed beyond a guard assertion; the `EnforcementTests` already prove the mechanism.

**Interfaces:**
- Consumes: `RequiresPermissionAttribute`, `IResourceScoped` (Task 3); `Permission`, `Scope` (Domain).

- [ ] **Step 1: Guard the Projects commands/query**

`src/DeveloperPlatform.Application/Projects/CreateProject/CreateProjectCommand.cs` — add attribute (a create has no instance yet, so tenant scope):

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Projects.CreateProject;

[RequiresPermission(Permission.ProjectsWrite)]
public record CreateProjectCommand(string Name, string? Description) : ICommand<CreateProjectResult>;

public record CreateProjectResult(Guid ProjectId);
```

`src/DeveloperPlatform.Application/Projects/DeleteProject/DeleteProjectCommand.cs` — guard + per-instance scope. Read the current file, then set it to:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Projects.DeleteProject;

[RequiresPermission(Permission.ProjectsWrite)]
public record DeleteProjectCommand(Guid ProjectId) : ICommand, IResourceScoped
{
    public Scope ResourceScope => Scope.Project(ProjectId);
}
```

(If the current `DeleteProjectCommand` has a different property name than `ProjectId`, keep its existing shape and only add the attribute + `IResourceScoped`/`ResourceScope` using the real property.)

`src/DeveloperPlatform.Application/Projects/GetProjects/GetProjectsQuery.cs` — guard read:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Projects.GetProjects;

[RequiresPermission(Permission.ProjectsRead)]
public record GetProjectsQuery : IQuery<IReadOnlyList<ProjectSummary>>;

public record ProjectSummary(Guid Id, string Name, string? Description, DateTime CreatedAt);
```

(Preserve the CURRENT `GetProjectsQuery`/`ProjectSummary` definition — read the file first — and only add the `using`s + `[RequiresPermission(Permission.ProjectsRead)]` attribute.)

- [ ] **Step 2: Guard the API-key command**

Read `src/DeveloperPlatform.Application/ApiKeys/CreateApiKey/CreateApiKeyCommand.cs`, then add the attribute + per-project scope. Add `using DeveloperPlatform.Application.Attributes;`, `using DeveloperPlatform.Application.Authorization;`, `using DeveloperPlatform.Domain.Authorization;`, put `[RequiresPermission(Permission.ApiKeysManage)]` on the command record, make it implement `IResourceScoped`, and add:

```csharp
    public Scope ResourceScope => Scope.Project(ProjectId);
```

(using the command's real project-id property name).

- [ ] **Step 3: Build + run the full authorization + dispatcher test set**

Run: `dotnet build developer-platform-reference.slnx --no-restore` → 0 errors.
Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Api.Tests.Authorization|FullyQualifiedName~Dispatching|FullyQualifiedName~Crypto|FullyQualifiedName~Context"`
Expected: PASS. (The 5 `WebApplicationFactory` integration tests in `Projects`/`Auth` are not in these namespaces; if run separately they need RabbitMQ — unrelated.)

- [ ] **Step 4: Run architecture tests**

Run: `dotnet test tests/DeveloperPlatform.ArchitectureTests`
Expected: PASS (10). The new Application interfaces/attributes reference only Domain; Infrastructure impls reference Application/Domain — no layering violation.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(authz): guard Projects and API-key endpoints with [RequiresPermission]"
```

---

## Self-Review

**1. Spec coverage (Slice 3 scope):**
- §5 execution-context change (`PrincipalId`/`PrincipalType`, keep `UserId`) — Task 2. ✅
- §5 principal resolution (JIT user + membership; bootstrap Owner) — Task 4. ✅
- §6 `IAuthorizationService` effective-permission resolution + scope cascade — Task 1. ✅
- §6 `[RequiresPermission]` dispatcher enforcement + `IResourceScoped` + `ForbiddenException`→403 (and 401 for missing tenant) — Task 3. ✅
- §6 guard existing endpoints (full-wiring decision) — Task 5. ✅
- Audit `AuditOutboxEntry`/`AuditEvent` touch-up + migration — Task 2. ✅
- **Deferred (documented):** privilege-escalation guard (Slice 5, no grant commands yet); machine/service-account principal resolution (Slice 4); composite `(PrincipalId, TenantId)` FK hardening (follow-up). ✅

**2. Placeholder scan:** No `TBD`/`TODO`. Code steps are complete; the two "read the current file first, preserve its shape" notes (DeleteProject/GetProjects/CreateApiKey) are because those files' exact property names must be honoured — the attribute/interface additions are fully specified. The audit migration is tool-generated with a verification grep.

**3. Type consistency:** `IExecutionContext` (`PrincipalId`/`PrincipalType`/`UserId`) is used identically in `HttpExecutionContext`, the dispatchers, the resolver, and all test contexts. `AuditOutboxEntry.Create`/`AuditEvent.Create`/`AuditMessage` all take `(…, principalId, principalType, userId, …)` in the same order across the dispatcher, relay, and consumer. `IAuthorizationService.AuthorizeAsync(principalId, permission, scope)`, `RequiresPermissionAttribute(Permission)`, `IResourceScoped.ResourceScope`, `IPrincipalResolver.ResolveAsync` + `ResolvedPrincipal(PrincipalId, Type, UserId)`, `AuthorizationService` ctor `(ApplicationDbContext)`, `CommandDispatcher` new last param `IAuthorizationService` — consistent across definitions, registrations, and tests.

**4. Risk notes for the executor:** Task 2 is the widest change — build after each sub-step. The middleware now performs DB writes (JIT) per first request; acceptable for a reference platform. The `first-member-becomes-Owner` bootstrap is a stated policy default; the dev token's user becomes Owner so the existing Projects flow keeps working.
