# Permission Catalog Implementation Plan (Authz Slice 1 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce a type-safe, single-source-of-truth permission catalog (`resource:action`) in the Domain layer and expose it via `GET /api/v1/permissions`.

**Architecture:** Three enums (`Resource`, `PermissionAction`, `Permission`) live in `DeveloperPlatform.Domain.Authorization`. Each `Permission` member carries a `[Perm(resource, action, description)]` attribute. A static `PermissionCatalog` reflects over the `Permission` enum **once** at startup to build descriptors; the canonical wire token (`"secrets:write"`) is *derived* from the resource/action identifiers, never hand-typed. A read-only API endpoint projects the catalog to JSON.

**Tech Stack:** .NET 10, C# (records, primary constructors), xUnit, ASP.NET Core Minimal APIs, Asp.Versioning.

## Where this fits (authorization subsystem roadmap)

This is **Slice 1 of 6**. Spec: `docs/specs/2026-08-25-permissions-member-management-design.md`.

1. **Permission catalog** ← this plan
2. Principal & grant domain model + persistence + seed system roles
3. `IAuthorizationService` + dispatcher enforcement + `IExecutionContext`/audit changes
4. API-key auth handler + service accounts + key credentials
5. Member invitations + hybrid onboarding + shared grant endpoints
6. Web Access UI (Members / Service Accounts / Roles)

Slice 1 has **no dependency on the DB or execution context**, so it ships cleanly on its own. Later slices consume `Permission` and `PermissionCatalog` from this slice.

## Global Constraints

Copied from the spec and repo conventions — every task implicitly includes these:

- **Target framework:** `net10.0`; `Nullable` enabled; `ImplicitUsings` enabled. Because `ImplicitUsings` pulls in `System`, **do not** name any type `Action` (collides with `System.Action`) — the action enum is `PermissionAction`.
- **Single source of truth:** no hand-typed `resource:action` permission strings anywhere. Tokens are derived by `PermissionCatalog` from enum identifiers.
- **Layering:** the catalog lives in `DeveloperPlatform.Domain` and must not reference `Application`, `Infrastructure`, or `Api` (enforced by `DomainLayerTests.Domain_Has_No_Outward_Dependencies`).
- **Commits:** the repo's `commit-msg` lefthook hook **rejects AI co-author trailers** — do NOT add `Co-Authored-By:` / `Claude-Session:` lines. Use Conventional Commits (`feat:`). The `pre-commit` hook runs `dotnet build` + architecture tests + `dotnet format`; a commit fails if the solution does not build or is unformatted. If a build complains about missing assets, run `dotnet restore developer-platform-reference.slnx` first.
- **Test framework:** xUnit (`[Fact]`, `Assert.*`). New tests go in the existing `tests/DeveloperPlatform.Api.Tests` project under an `Authorization/` folder.

---

## File Structure

**Created:**
- `src/DeveloperPlatform.Domain/Authorization/Resource.cs` — resource enum
- `src/DeveloperPlatform.Domain/Authorization/PermissionAction.cs` — action enum
- `src/DeveloperPlatform.Domain/Authorization/TokenAttribute.cs` — optional wire-token override for a `Resource`/`PermissionAction` member
- `src/DeveloperPlatform.Domain/Authorization/PermAttribute.cs` — metadata attribute on `Permission` members
- `src/DeveloperPlatform.Domain/Authorization/Permission.cs` — the permission enum (single source of truth)
- `src/DeveloperPlatform.Domain/Authorization/PermissionDescriptor.cs` — resolved metadata record
- `src/DeveloperPlatform.Domain/Authorization/PermissionCatalog.cs` — static reflection-built catalog + token mapping
- `src/DeveloperPlatform.Api/Endpoints/Permissions/PermissionsEndpoints.cs` — `GET /api/v1/permissions`
- `tests/DeveloperPlatform.Api.Tests/Authorization/PermissionCatalogTests.cs`
- `tests/DeveloperPlatform.Api.Tests/Authorization/PermissionsEndpointTests.cs`

**Modified:**
- `src/DeveloperPlatform.Api/Program.cs` — register `MapPermissions(versionSet)`

---

## Task 1: Permission catalog (types + reflection catalog)

The enums, attributes, descriptor, and `PermissionCatalog` are one cohesive deliverable — the enums/attributes are meaningless without the catalog that reads them, so they share a test cycle.

**Files:**
- Create: `src/DeveloperPlatform.Domain/Authorization/Resource.cs`
- Create: `src/DeveloperPlatform.Domain/Authorization/PermissionAction.cs`
- Create: `src/DeveloperPlatform.Domain/Authorization/TokenAttribute.cs`
- Create: `src/DeveloperPlatform.Domain/Authorization/PermAttribute.cs`
- Create: `src/DeveloperPlatform.Domain/Authorization/Permission.cs`
- Create: `src/DeveloperPlatform.Domain/Authorization/PermissionDescriptor.cs`
- Create: `src/DeveloperPlatform.Domain/Authorization/PermissionCatalog.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Authorization/PermissionCatalogTests.cs`

**Interfaces:**
- Consumes: nothing (pure Domain, no earlier tasks).
- Produces:
  - `enum Permission { ProjectsRead, ProjectsWrite, SecretsRead, SecretsWrite, ApiKeysManage, MembersManage, ServiceAccountsManage, RolesManage, AuditRead }`
  - `static class PermissionCatalog` with:
    - `IReadOnlyList<PermissionDescriptor> All`
    - `PermissionDescriptor Describe(Permission permission)`
    - `string ToToken(Permission permission)`
    - `Permission FromToken(string token)` (throws `ArgumentException` on unknown)
  - `sealed record PermissionDescriptor(Permission Permission, Resource Resource, PermissionAction Action, string Token, string Description)`

- [ ] **Step 1: Write the failing catalog tests**

Create `tests/DeveloperPlatform.Api.Tests/Authorization/PermissionCatalogTests.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class PermissionCatalogTests
{
    [Fact]
    public void All_Has_One_Descriptor_Per_Enum_Value()
    {
        var enumCount = Enum.GetValues<Permission>().Length;
        Assert.Equal(enumCount, PermissionCatalog.All.Count);
    }

    [Fact]
    public void Every_Descriptor_Has_NonEmpty_Token_And_Description()
    {
        Assert.All(PermissionCatalog.All, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Token));
            Assert.False(string.IsNullOrWhiteSpace(d.Description));
        });
    }

    [Fact]
    public void Token_Is_Derived_As_Resource_Colon_Action_Lowercased()
    {
        Assert.Equal("secrets:write", PermissionCatalog.ToToken(Permission.SecretsWrite));
        Assert.Equal("projects:read", PermissionCatalog.ToToken(Permission.ProjectsRead));
        Assert.Equal("apikeys:manage", PermissionCatalog.ToToken(Permission.ApiKeysManage));
    }

    [Fact]
    public void Tokens_Are_Unique()
    {
        var tokens = PermissionCatalog.All.Select(d => d.Token).ToList();
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public void ToToken_FromToken_RoundTrips_For_All_Permissions()
    {
        foreach (var permission in Enum.GetValues<Permission>())
        {
            var token = PermissionCatalog.ToToken(permission);
            Assert.Equal(permission, PermissionCatalog.FromToken(token));
        }
    }

    [Fact]
    public void FromToken_Throws_For_Unknown_Token()
    {
        Assert.Throws<ArgumentException>(() => PermissionCatalog.FromToken("nope:nope"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~PermissionCatalogTests"`
Expected: FAIL — does not compile (`Permission`, `PermissionCatalog` do not exist yet).

- [ ] **Step 3: Create the enums and attributes**

`src/DeveloperPlatform.Domain/Authorization/Resource.cs`:

```csharp
namespace DeveloperPlatform.Domain.Authorization;

public enum Resource
{
    Projects,
    Secrets,
    ApiKeys,
    Members,
    Roles,
    ServiceAccounts,
    Audit,
}
```

`src/DeveloperPlatform.Domain/Authorization/PermissionAction.cs`:

```csharp
namespace DeveloperPlatform.Domain.Authorization;

// Named PermissionAction (not Action) to avoid colliding with System.Action under ImplicitUsings.
public enum PermissionAction
{
    Read,
    Write,
    Manage,
    Delete,
}
```

`src/DeveloperPlatform.Domain/Authorization/TokenAttribute.cs`:

```csharp
namespace DeveloperPlatform.Domain.Authorization;

// Optional override for the wire token of a Resource/PermissionAction member.
// Used only when the derived (lowercased identifier) token is not desired,
// e.g. [Token("service-accounts")] on a multi-word member.
[AttributeUsage(AttributeTargets.Field)]
public sealed class TokenAttribute(string token) : Attribute
{
    public string Token { get; } = token;
}
```

`src/DeveloperPlatform.Domain/Authorization/PermAttribute.cs`:

```csharp
namespace DeveloperPlatform.Domain.Authorization;

[AttributeUsage(AttributeTargets.Field)]
public sealed class PermAttribute(Resource resource, PermissionAction action, string description) : Attribute
{
    public Resource Resource { get; } = resource;
    public PermissionAction Action { get; } = action;
    public string Description { get; } = description;
}
```

`src/DeveloperPlatform.Domain/Authorization/Permission.cs`:

```csharp
namespace DeveloperPlatform.Domain.Authorization;

// SINGLE SOURCE OF TRUTH for the permission vocabulary.
// The wire token ("resource:action") is derived by PermissionCatalog — never hand-typed.
public enum Permission
{
    [Perm(Resource.Projects, PermissionAction.Read,  "View projects")]              ProjectsRead,
    [Perm(Resource.Projects, PermissionAction.Write, "Create and edit projects")]   ProjectsWrite,
    [Perm(Resource.Secrets,  PermissionAction.Read,  "Read secret values")]         SecretsRead,
    [Perm(Resource.Secrets,  PermissionAction.Write, "Set and rotate secrets")]     SecretsWrite,
    [Perm(Resource.ApiKeys,  PermissionAction.Manage, "Manage API keys")]           ApiKeysManage,
    [Perm(Resource.Members,  PermissionAction.Manage, "Invite and remove members")] MembersManage,
    [Perm(Resource.ServiceAccounts, PermissionAction.Manage, "Manage service accounts")] ServiceAccountsManage,
    [Perm(Resource.Roles,    PermissionAction.Manage, "Assign roles and permissions")] RolesManage,
    [Perm(Resource.Audit,    PermissionAction.Read,  "View the audit log")]          AuditRead,
}
```

`src/DeveloperPlatform.Domain/Authorization/PermissionDescriptor.cs`:

```csharp
namespace DeveloperPlatform.Domain.Authorization;

public sealed record PermissionDescriptor(
    Permission Permission,
    Resource Resource,
    PermissionAction Action,
    string Token,
    string Description);
```

- [ ] **Step 4: Implement `PermissionCatalog`**

`src/DeveloperPlatform.Domain/Authorization/PermissionCatalog.cs`:

```csharp
using System.Reflection;

namespace DeveloperPlatform.Domain.Authorization;

// Reflects over the Permission enum ONCE to build descriptors and the token map.
// Static because it is derived purely from compile-time enum metadata.
public static class PermissionCatalog
{
    private static readonly IReadOnlyDictionary<Permission, PermissionDescriptor> ByPermission = Build();

    private static readonly IReadOnlyList<PermissionDescriptor> AllDescriptors =
        ByPermission.Values.OrderBy(d => d.Token, StringComparer.Ordinal).ToList();

    private static readonly IReadOnlyDictionary<string, Permission> ByToken =
        ByPermission.Values.ToDictionary(d => d.Token, d => d.Permission);

    public static IReadOnlyList<PermissionDescriptor> All => AllDescriptors;

    public static PermissionDescriptor Describe(Permission permission) => ByPermission[permission];

    public static string ToToken(Permission permission) => ByPermission[permission].Token;

    public static Permission FromToken(string token) =>
        ByToken.TryGetValue(token, out var permission)
            ? permission
            : throw new ArgumentException($"Unknown permission token '{token}'.", nameof(token));

    private static IReadOnlyDictionary<Permission, PermissionDescriptor> Build()
    {
        var map = new Dictionary<Permission, PermissionDescriptor>();

        foreach (var permission in Enum.GetValues<Permission>())
        {
            var field = typeof(Permission).GetField(permission.ToString())!;
            var perm = field.GetCustomAttribute<PermAttribute>()
                ?? throw new InvalidOperationException(
                    $"Permission '{permission}' is missing a [Perm] attribute.");

            var token = $"{TokenOf(perm.Resource)}:{TokenOf(perm.Action)}";
            map[permission] = new PermissionDescriptor(
                permission, perm.Resource, perm.Action, token, perm.Description);
        }

        return map;
    }

    // Wire token for a Resource/PermissionAction member: an explicit [Token] override,
    // else the lowercased enum identifier.
    private static string TokenOf<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var field = typeof(TEnum).GetField(value.ToString())!;
        var overrideToken = field.GetCustomAttribute<TokenAttribute>();
        return overrideToken?.Token ?? value.ToString().ToLowerInvariant();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~PermissionCatalogTests"`
Expected: PASS (6 tests).

- [ ] **Step 6: Verify architecture tests still pass**

Run: `dotnet test tests/DeveloperPlatform.ArchitectureTests`
Expected: PASS — the new types are pure Domain with no outward dependencies, and none implement `IEntity`, so `DomainLayerTests` is unaffected.

- [ ] **Step 7: Commit**

```bash
git add src/DeveloperPlatform.Domain/Authorization tests/DeveloperPlatform.Api.Tests/Authorization/PermissionCatalogTests.cs
git commit -m "feat(authz): type-safe permission catalog with derived tokens"
```

---

## Task 2: `GET /api/v1/permissions` endpoint

Exposes the catalog. The catalog→DTO projection is a pure static method so it is unit-tested without booting the app (the app's DI eagerly connects to RabbitMQ at startup, so a full `WebApplicationFactory` boot is intentionally avoided here).

**Files:**
- Create: `src/DeveloperPlatform.Api/Endpoints/Permissions/PermissionsEndpoints.cs`
- Modify: `src/DeveloperPlatform.Api/Program.cs` (add `using` + `app.MapPermissions(versionSet);`)
- Test: `tests/DeveloperPlatform.Api.Tests/Authorization/PermissionsEndpointTests.cs`

**Interfaces:**
- Consumes: `PermissionCatalog.All`, `PermissionDescriptor` (Task 1).
- Produces:
  - `static IReadOnlyList<PermissionResponse> PermissionsEndpoints.BuildResponse()`
  - `record PermissionResponse(string Token, string Resource, string Action, string Description)`
  - extension `IEndpointRouteBuilder MapPermissions(this IEndpointRouteBuilder app, ApiVersionSet versionSet)`

- [ ] **Step 1: Write the failing endpoint projection test**

Create `tests/DeveloperPlatform.Api.Tests/Authorization/PermissionsEndpointTests.cs`:

```csharp
using DeveloperPlatform.Api.Endpoints.Permissions;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class PermissionsEndpointTests
{
    [Fact]
    public void BuildResponse_Returns_One_Row_Per_Catalog_Permission()
    {
        var response = PermissionsEndpoints.BuildResponse();
        Assert.Equal(PermissionCatalog.All.Count, response.Count);
    }

    [Fact]
    public void BuildResponse_Projects_SecretsWrite_With_Derived_Token()
    {
        var response = PermissionsEndpoints.BuildResponse();

        var row = Assert.Single(response, r => r.Token == "secrets:write");
        Assert.Equal("Secrets", row.Resource);
        Assert.Equal("Write", row.Action);
        Assert.False(string.IsNullOrWhiteSpace(row.Description));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~PermissionsEndpointTests"`
Expected: FAIL — does not compile (`PermissionsEndpoints` does not exist).

- [ ] **Step 3: Create the endpoint**

`src/DeveloperPlatform.Api/Endpoints/Permissions/PermissionsEndpoints.cs`:

```csharp
using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Api.Endpoints.Permissions;

public static class PermissionsEndpoints
{
    public static IEndpointRouteBuilder MapPermissions(
        this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        app.MapGet("/api/v1/permissions", () => Results.Ok(BuildResponse()))
            .WithName("GetPermissions")
            .WithTags("Permissions")
            .WithSummary("List the permission catalog")
            .WithDescription("Returns every permission the platform enforces, as stable resource:action tokens.")
            .Produces<IReadOnlyList<PermissionResponse>>(StatusCodes.Status200OK)
            .WithApiVersionSet(versionSet)
            .MapToApiVersion(1)
            .RequireAuthorization();

        return app;
    }

    // Pure projection — unit-tested without booting the app.
    public static IReadOnlyList<PermissionResponse> BuildResponse() =>
        PermissionCatalog.All
            .Select(d => new PermissionResponse(
                d.Token,
                d.Resource.ToString(),
                d.Action.ToString(),
                d.Description))
            .ToList();

    public record PermissionResponse(string Token, string Resource, string Action, string Description);
}
```

- [ ] **Step 4: Register the endpoint in `Program.cs`**

In `src/DeveloperPlatform.Api/Program.cs`, add the using alongside the other endpoint usings (after line 3, `using DeveloperPlatform.Api.Endpoints.Projects;`):

```csharp
using DeveloperPlatform.Api.Endpoints.Permissions;
```

Then register it next to the other `Map*` calls (after `app.MapProjects(versionSet);`, around line 104):

```csharp
    app.MapPermissions(versionSet);
```

- [ ] **Step 5: Run the endpoint tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~PermissionsEndpointTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Build the whole solution to confirm `Program.cs` compiles**

Run: `dotnet build developer-platform-reference.slnx --no-restore`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/DeveloperPlatform.Api/Endpoints/Permissions/PermissionsEndpoints.cs src/DeveloperPlatform.Api/Program.cs tests/DeveloperPlatform.Api.Tests/Authorization/PermissionsEndpointTests.cs
git commit -m "feat(authz): expose GET /api/v1/permissions catalog endpoint"
```

---

## Self-Review

**1. Spec coverage (Slice 1 scope only):**
- Spec §4 "Permission catalog (single source of truth)" — covered by Task 1 (`Resource`/`PermissionAction`/`Permission` enums, `PermAttribute`, `TokenAttribute`, `PermissionCatalog` with derived tokens). ✅
- Spec §7 "Catalog (read): `GET /api/v1/permissions`" — covered by Task 2. ✅
- Spec §4 EF `HasConversion` persistence of the token — **intentionally deferred to Slice 2** (there is no persisted entity referencing `Permission` in this slice); `ToToken`/`FromToken` are provided now so Slice 2's value converter can consume them. Noted, not a gap.
- Spec `GET /roles` — belongs to Slice 2 (needs the `Role` entity + seed). Out of scope here.

**2. Placeholder scan:** No `TBD`/`TODO`/"add error handling"/"similar to". Every code step contains complete, compilable code. ✅

**3. Type consistency:** `Permission`, `PermissionAction`, `Resource`, `PermAttribute`, `TokenAttribute`, `PermissionDescriptor`, `PermissionCatalog.{All,Describe,ToToken,FromToken}`, `PermissionsEndpoints.{BuildResponse,MapPermissions,PermissionResponse}` are used identically in the tests, definitions, and endpoint. `PermissionAction` (not `Action`) is used consistently to avoid the `System.Action` clash. ✅

**4. Deferred/uncovered later slices:** Principal/grant model, enforcement, execution-context change, API keys, members, and Web UI are explicitly Slices 2–6 and are out of scope for this plan.
