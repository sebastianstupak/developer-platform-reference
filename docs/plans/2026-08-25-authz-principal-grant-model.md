# Principal & Grant Model Implementation Plan (Authz Slice 2 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the authorization data model — global `User`, the unified `Principal` (with `Membership`/`ServiceAccount` linked to it), roles, and scoped grants — persist it with EF Core + MySQL, and seed the built-in system roles.

**Architecture:** `Principal` is a real table (Option A). `Membership` and `ServiceAccount` each carry a unique FK to `Principal`, and `RoleAssignment`/`PermissionGrant`/`Invitation` FK to `Principal.Id`, so the database enforces referential integrity on grants. No EF inheritance is used (it would collide with the tenant query-filter that auto-applies to every concrete `ITenantScoped` type). `User` and `Role` are global (`IEntity`, not tenant-scoped), like the existing `Tenant`. Permissions persist as their canonical `resource:action` token via a reused value converter over the Slice 1 `PermissionCatalog`.

**Tech Stack:** .NET 10, C# (records, primary constructors), EF Core 10 + Pomelo MySQL, xUnit + EF InMemory.

## Where this fits (authorization subsystem roadmap)

**Slice 2 of 6.** Spec: `docs/specs/2026-08-25-permissions-member-management-design.md`. Depends on Slice 1 (merged): `DeveloperPlatform.Domain.Authorization.{Permission, PermissionCatalog}`.

1. Permission catalog ✅ (merged)
2. **Principal & grant domain model + persistence + seed system roles** ← this plan
3. `IAuthorizationService` + dispatcher enforcement + `IExecutionContext`/audit changes
4. API-key auth handler + service accounts + key credentials
5. Member invitations + hybrid onboarding + grant endpoints
6. Web Access UI

This slice ships the schema + seed only. No endpoints, no enforcement (Slice 3), no behavior change to existing routes.

## Global Constraints

- **Target framework** `net10.0`; `Nullable` enabled; `ImplicitUsings` enabled. Do NOT name any type `Action` (collides with `System.Action`).
- **Layering:** entities live in `DeveloperPlatform.Domain`; EF configs/migrations in `DeveloperPlatform.Infrastructure`. Domain must not reference `Application`/`Infrastructure`/`Api` (enforced by `DomainLayerTests`).
- **Global (non-tenant) entities** are `IEntity` only, never `ITenantScoped`: `User`, `Role`. Everything else that is a concrete `IEntity` MUST be `ITenantScoped` (via `TenantEntity`) — enforced by `DomainLayerTests.All_Concrete_Domain_Entities_Implement_ITenantScoped`, whose exclusion list this slice updates.
- **Permissions persist as their derived token string** (`PermissionCatalog.ToToken`/`FromToken`) — never as an int or a hand-typed string.
- **Entity conventions (match existing `Project`/`Secret`/`ProjectEnvironment`):** `public` class, `private` parameterless ctor, `private set` properties, static `Create*` factories that validate with `ArgumentException.ThrowIfNullOrWhiteSpace`, mutation via methods.
- **Commits:** the `commit-msg` lefthook hook REJECTS AI co-author trailers — no `Co-Authored-By:`/`Claude-Session:` lines. Conventional Commits (`feat(authz): ...`). The `pre-commit` hook runs `dotnet build` + architecture tests + `dotnet format` (~50s); if a build reports missing assets run `dotnet restore developer-platform-reference.slnx` first. Do NOT use `--no-verify`.
- **The migration step needs MySQL running:** the design-time factory uses `ServerVersion.AutoDetect(connectionString)`, which opens a connection. Run `docker compose up -d db` before `dotnet ef migrations add`.
- **Test framework** xUnit. New tests under `tests/DeveloperPlatform.Api.Tests/Authorization/`.

---

## File Structure

**Created — Domain:**
- `src/DeveloperPlatform.Domain/Authorization/ScopeType.cs` — enum
- `src/DeveloperPlatform.Domain/Authorization/Scope.cs` — value object (`readonly record struct`) + `Encompasses`
- `src/DeveloperPlatform.Domain/Authorization/PrincipalType.cs` — enum
- `src/DeveloperPlatform.Domain/Authorization/MembershipStatus.cs` — enum
- `src/DeveloperPlatform.Domain/Authorization/InvitationStatus.cs` — enum
- `src/DeveloperPlatform.Domain/Identity/User.cs` — global identity entity
- `src/DeveloperPlatform.Domain/Authorization/Principal.cs`
- `src/DeveloperPlatform.Domain/Authorization/Membership.cs`
- `src/DeveloperPlatform.Domain/Authorization/ServiceAccount.cs`
- `src/DeveloperPlatform.Domain/Authorization/Role.cs`
- `src/DeveloperPlatform.Domain/Authorization/RolePermission.cs`
- `src/DeveloperPlatform.Domain/Authorization/RoleAssignment.cs`
- `src/DeveloperPlatform.Domain/Authorization/PermissionGrant.cs`
- `src/DeveloperPlatform.Domain/Authorization/Invitation.cs`

**Created — Infrastructure:**
- `src/DeveloperPlatform.Infrastructure/Persistence/Converters/PermissionTokenConverter.cs`
- `src/DeveloperPlatform.Infrastructure/Authorization/SystemRoles.cs` — fixed seed definitions
- `src/DeveloperPlatform.Infrastructure/Persistence/Configurations/UserConfiguration.cs`
- `.../Configurations/PrincipalConfiguration.cs`
- `.../Configurations/MembershipConfiguration.cs`
- `.../Configurations/ServiceAccountConfiguration.cs`
- `.../Configurations/RoleConfiguration.cs`
- `.../Configurations/RolePermissionConfiguration.cs`
- `.../Configurations/RoleAssignmentConfiguration.cs`
- `.../Configurations/PermissionGrantConfiguration.cs`
- `.../Configurations/InvitationConfiguration.cs`
- `src/DeveloperPlatform.Infrastructure/Migrations/<generated>_AddAuthorizationModel.cs` (generated)

**Modified:**
- `src/DeveloperPlatform.Infrastructure/Persistence/ApplicationDbContext.cs` — add DbSets
- `tests/DeveloperPlatform.ArchitectureTests/DomainLayerTests.cs` — exclude `User`, `Role`

**Tests:**
- `tests/DeveloperPlatform.Api.Tests/Authorization/ScopeTests.cs`
- `tests/DeveloperPlatform.Api.Tests/Authorization/PrincipalModelTests.cs`
- `tests/DeveloperPlatform.Api.Tests/Authorization/GrantModelTests.cs`
- `tests/DeveloperPlatform.Api.Tests/Authorization/AuthorizationPersistenceTests.cs`

---

## Task 1: Scope value object + enums

**Files:**
- Create: `src/DeveloperPlatform.Domain/Authorization/ScopeType.cs`, `Scope.cs`, `PrincipalType.cs`, `MembershipStatus.cs`, `InvitationStatus.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Authorization/ScopeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum ScopeType { Tenant, Project, Environment }`
  - `readonly record struct Scope(ScopeType Type, Guid? TargetId)` with statics `Scope.Tenant`, `Scope.Project(Guid)`, `Scope.Environment(Guid)`, method `bool Encompasses(Scope other)`, and validation via `Scope.Create`.
  - `enum PrincipalType { Member, ServiceAccount }`, `enum MembershipStatus { Invited, Active, Suspended }`, `enum InvitationStatus { Pending, Accepted, Revoked, Expired }`

- [ ] **Step 1: Write the failing Scope tests**

Create `tests/DeveloperPlatform.Api.Tests/Authorization/ScopeTests.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class ScopeTests
{
    private static readonly Guid Proj = Guid.NewGuid();
    private static readonly Guid Env = Guid.NewGuid();

    [Fact]
    public void Tenant_Scope_Has_No_Target()
    {
        var s = Scope.Tenant;
        Assert.Equal(ScopeType.Tenant, s.Type);
        Assert.Null(s.TargetId);
    }

    [Fact]
    public void Project_And_Environment_Require_A_Target()
    {
        Assert.Equal(Proj, Scope.Project(Proj).TargetId);
        Assert.Throws<ArgumentException>(() => Scope.Create(ScopeType.Project, null));
        Assert.Throws<ArgumentException>(() => Scope.Create(ScopeType.Environment, null));
    }

    [Fact]
    public void Tenant_Scope_Rejects_A_Target()
    {
        Assert.Throws<ArgumentException>(() => Scope.Create(ScopeType.Tenant, Proj));
    }

    [Fact]
    public void Tenant_Encompasses_Everything()
    {
        Assert.True(Scope.Tenant.Encompasses(Scope.Tenant));
        Assert.True(Scope.Tenant.Encompasses(Scope.Project(Proj)));
        Assert.True(Scope.Tenant.Encompasses(Scope.Environment(Env)));
    }

    [Fact]
    public void Project_Encompasses_Only_Itself()
    {
        var p = Scope.Project(Proj);
        Assert.True(p.Encompasses(Scope.Project(Proj)));
        Assert.False(p.Encompasses(Scope.Tenant));
        Assert.False(p.Encompasses(Scope.Project(Guid.NewGuid())));
        // Environment-under-a-project cascade is resolved by the authorization service (Slice 3),
        // which knows an environment's parent project; Scope alone treats them as distinct targets.
        Assert.False(p.Encompasses(Scope.Environment(Env)));
    }

    [Fact]
    public void Environment_Encompasses_Only_Itself()
    {
        var e = Scope.Environment(Env);
        Assert.True(e.Encompasses(Scope.Environment(Env)));
        Assert.False(e.Encompasses(Scope.Environment(Guid.NewGuid())));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~ScopeTests"`
Expected: FAIL — `Scope`/`ScopeType` do not exist.

- [ ] **Step 3: Create the enums**

`src/DeveloperPlatform.Domain/Authorization/ScopeType.cs`:

```csharp
namespace DeveloperPlatform.Domain.Authorization;

public enum ScopeType { Tenant, Project, Environment }
```

`src/DeveloperPlatform.Domain/Authorization/PrincipalType.cs`:

```csharp
namespace DeveloperPlatform.Domain.Authorization;

public enum PrincipalType { Member, ServiceAccount }
```

`src/DeveloperPlatform.Domain/Authorization/MembershipStatus.cs`:

```csharp
namespace DeveloperPlatform.Domain.Authorization;

public enum MembershipStatus { Invited, Active, Suspended }
```

`src/DeveloperPlatform.Domain/Authorization/InvitationStatus.cs`:

```csharp
namespace DeveloperPlatform.Domain.Authorization;

public enum InvitationStatus { Pending, Accepted, Revoked, Expired }
```

- [ ] **Step 4: Create the Scope value object**

`src/DeveloperPlatform.Domain/Authorization/Scope.cs`:

```csharp
namespace DeveloperPlatform.Domain.Authorization;

// A permission/role grant scope: tenant-wide, or pinned to a project or environment.
public readonly record struct Scope
{
    public ScopeType Type { get; }
    public Guid? TargetId { get; }

    private Scope(ScopeType type, Guid? targetId)
    {
        Type = type;
        TargetId = targetId;
    }

    public static Scope Tenant { get; } = new(ScopeType.Tenant, null);
    public static Scope Project(Guid projectId) => Create(ScopeType.Project, projectId);
    public static Scope Environment(Guid environmentId) => Create(ScopeType.Environment, environmentId);

    public static Scope Create(ScopeType type, Guid? targetId)
    {
        if (type == ScopeType.Tenant && targetId is not null)
        {
            throw new ArgumentException("Tenant scope must not have a target id.", nameof(targetId));
        }

        if (type != ScopeType.Tenant && (targetId is null || targetId == Guid.Empty))
        {
            throw new ArgumentException($"{type} scope requires a non-empty target id.", nameof(targetId));
        }

        return new Scope(type, targetId);
    }

    // True when this scope is an ancestor-or-equal of `other` in the scope hierarchy.
    // Tenant ⊇ any Project ⊇ its Environments. Project→Environment nesting is resolved by the
    // authorization service (which knows an environment's parent project); Scope compares by identity.
    public bool Encompasses(Scope other) => this switch
    {
        { Type: ScopeType.Tenant } => true,
        _ => Type == other.Type && TargetId == other.TargetId,
    };
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~ScopeTests"`
Expected: PASS (6 tests).

- [ ] **Step 6: Commit**

```bash
git add src/DeveloperPlatform.Domain/Authorization tests/DeveloperPlatform.Api.Tests/Authorization/ScopeTests.cs
git commit -m "feat(authz): scope value object and principal/membership/invitation enums"
```

---

## Task 2: User, Principal, Membership, ServiceAccount entities

**Files:**
- Create: `src/DeveloperPlatform.Domain/Identity/User.cs`
- Create: `src/DeveloperPlatform.Domain/Authorization/Principal.cs`, `Membership.cs`, `ServiceAccount.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Authorization/PrincipalModelTests.cs`

**Interfaces:**
- Consumes: `PrincipalType`, `MembershipStatus` (Task 1); `TenantEntity`, `IEntity` (existing).
- Produces:
  - `User.Create(string keycloakSubject, string email, string displayName)` → `User { Guid Id, string KeycloakSubject, string Email, string DisplayName, DateTime CreatedAt }`
  - `Principal.CreateMember(Guid tenantId, string displayName)` / `Principal.CreateServiceAccount(Guid tenantId, string displayName)` → `Principal { Id, TenantId, string DisplayName, PrincipalType Type }`; method `Rename(string displayName)`
  - `Membership.Create(Guid tenantId, Guid principalId, Guid userId, MembershipStatus status)` → `Membership { Id, TenantId, Guid PrincipalId, Guid UserId, MembershipStatus Status }`; methods `Activate()`, `Suspend()`
  - `ServiceAccount.Create(Guid tenantId, Guid principalId, string name, string? description)` → `ServiceAccount { Id, TenantId, Guid PrincipalId, string Name, string? Description }`

- [ ] **Step 1: Write the failing tests**

Create `tests/DeveloperPlatform.Api.Tests/Authorization/PrincipalModelTests.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class PrincipalModelTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void User_Create_Sets_Fields_And_Requires_Subject()
    {
        var u = User.Create("kc-sub-123", "dev@example.com", "Dev User");
        Assert.NotEqual(Guid.Empty, u.Id);
        Assert.Equal("kc-sub-123", u.KeycloakSubject);
        Assert.Equal("dev@example.com", u.Email);
        Assert.Equal("Dev User", u.DisplayName);
        Assert.Throws<ArgumentException>(() => User.Create("  ", "e@x.com", "n"));
    }

    [Fact]
    public void Principal_CreateMember_Is_Member_Type()
    {
        var p = Principal.CreateMember(Tenant, "Dev User");
        Assert.Equal(Tenant, p.TenantId);
        Assert.Equal(PrincipalType.Member, p.Type);
        Assert.Equal("Dev User", p.DisplayName);
    }

    [Fact]
    public void Principal_CreateServiceAccount_Is_ServiceAccount_Type()
    {
        var p = Principal.CreateServiceAccount(Tenant, "ci-deployer");
        Assert.Equal(PrincipalType.ServiceAccount, p.Type);
    }

    [Fact]
    public void Membership_Create_Links_Principal_And_User()
    {
        var principalId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var m = Membership.Create(Tenant, principalId, userId, MembershipStatus.Invited);
        Assert.Equal(principalId, m.PrincipalId);
        Assert.Equal(userId, m.UserId);
        Assert.Equal(MembershipStatus.Invited, m.Status);

        m.Activate();
        Assert.Equal(MembershipStatus.Active, m.Status);
        m.Suspend();
        Assert.Equal(MembershipStatus.Suspended, m.Status);
    }

    [Fact]
    public void ServiceAccount_Create_Requires_Name()
    {
        var principalId = Guid.NewGuid();
        var sa = ServiceAccount.Create(Tenant, principalId, "ci-deployer", "CI robot");
        Assert.Equal(principalId, sa.PrincipalId);
        Assert.Equal("ci-deployer", sa.Name);
        Assert.Throws<ArgumentException>(() => ServiceAccount.Create(Tenant, principalId, " ", null));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~PrincipalModelTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Create `User`**

`src/DeveloperPlatform.Domain/Identity/User.cs`:

```csharp
using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Identity;

// Global identity, keyed by the Keycloak subject. NOT tenant-scoped — a user may belong to
// several tenants (via Membership). JIT-created on first login (Slice 5).
public class User : IEntity
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public string KeycloakSubject { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;

    private User() { }

    public static User Create(string keycloakSubject, string email, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keycloakSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return new User
        {
            KeycloakSubject = keycloakSubject,
            Email = email,
            DisplayName = displayName
        };
    }
}
```

- [ ] **Step 4: Create `Principal`, `Membership`, `ServiceAccount`**

`src/DeveloperPlatform.Domain/Authorization/Principal.cs`:

```csharp
using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Authorization;

// The unit that holds grants and is named in the audit trail. A Member or a ServiceAccount is a Principal.
// Membership/ServiceAccount each reference one Principal; grants FK to Principal.Id.
public class Principal : TenantEntity
{
    public string DisplayName { get; private set; } = string.Empty;
    public PrincipalType Type { get; private set; }

    private Principal() { }

    public static Principal CreateMember(Guid tenantId, string displayName) =>
        Create(tenantId, displayName, PrincipalType.Member);

    public static Principal CreateServiceAccount(Guid tenantId, string displayName) =>
        Create(tenantId, displayName, PrincipalType.ServiceAccount);

    private static Principal Create(Guid tenantId, string displayName, PrincipalType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return new Principal { TenantId = tenantId, DisplayName = displayName, Type = type };
    }

    public void Rename(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
    }
}
```

`src/DeveloperPlatform.Domain/Authorization/Membership.cs`:

```csharp
using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Authorization;

// A human principal within a tenant: links a global User to a Principal.
public class Membership : TenantEntity
{
    public Guid PrincipalId { get; private set; }
    public Guid UserId { get; private set; }
    public MembershipStatus Status { get; private set; }

    private Membership() { }

    public static Membership Create(Guid tenantId, Guid principalId, Guid userId, MembershipStatus status)
    {
        return new Membership
        {
            TenantId = tenantId,
            PrincipalId = principalId,
            UserId = userId,
            Status = status
        };
    }

    public void Activate() => Status = MembershipStatus.Active;
    public void Suspend() => Status = MembershipStatus.Suspended;
}
```

`src/DeveloperPlatform.Domain/Authorization/ServiceAccount.cs`:

```csharp
using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Authorization;

// A machine principal within a tenant. API key credentials (Slice 4) authenticate as it.
public class ServiceAccount : TenantEntity
{
    public Guid PrincipalId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private ServiceAccount() { }

    public static ServiceAccount Create(Guid tenantId, Guid principalId, string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ServiceAccount
        {
            TenantId = tenantId,
            PrincipalId = principalId,
            Name = name,
            Description = description
        };
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~PrincipalModelTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add src/DeveloperPlatform.Domain/Identity src/DeveloperPlatform.Domain/Authorization tests/DeveloperPlatform.Api.Tests/Authorization/PrincipalModelTests.cs
git commit -m "feat(authz): User, Principal, Membership, ServiceAccount entities"
```

---

## Task 3: Role, RolePermission, RoleAssignment, PermissionGrant, Invitation

**Files:**
- Create: `src/DeveloperPlatform.Domain/Authorization/Role.cs`, `RolePermission.cs`, `RoleAssignment.cs`, `PermissionGrant.cs`, `Invitation.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Authorization/GrantModelTests.cs`

**Interfaces:**
- Consumes: `Permission` (Slice 1); `Scope`, `ScopeType`, `InvitationStatus` (Task 1); `TenantEntity`, `IEntity`.
- Produces:
  - `Role.CreateSystem(Guid id, string name, DateTime createdAt)` → `Role { Guid Id, string Name, bool IsSystem, DateTime CreatedAt }`
  - `RolePermission.Create(Guid roleId, Permission permission)` → `RolePermission { Guid RoleId, Permission Permission }`
  - `RoleAssignment.Create(Guid tenantId, Guid principalId, Guid roleId, Scope scope)` → `RoleAssignment { Id, TenantId, Guid PrincipalId, Guid RoleId, ScopeType ScopeType, Guid? ScopeTargetId, Scope Scope }`
  - `PermissionGrant.Create(Guid tenantId, Guid principalId, Permission permission, Scope scope)` → `PermissionGrant { Id, TenantId, Guid PrincipalId, Permission Permission, ScopeType ScopeType, Guid? ScopeTargetId, Scope Scope }`
  - `Invitation.Create(Guid tenantId, string email, Guid roleId, Scope scope, string token, DateTime expiresAt)` → `Invitation { Id, TenantId, string Email, Guid RoleId, ScopeType ScopeType, Guid? ScopeTargetId, string Token, InvitationStatus Status, DateTime ExpiresAt }`; methods `Accept()`, `Revoke()`

- [ ] **Step 1: Write the failing tests**

Create `tests/DeveloperPlatform.Api.Tests/Authorization/GrantModelTests.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class GrantModelTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Principal = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid Proj = Guid.NewGuid();
    private static readonly DateTime Seed = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Role_CreateSystem_Is_System()
    {
        var r = Role.CreateSystem(RoleId, "Owner", Seed);
        Assert.Equal(RoleId, r.Id);
        Assert.Equal("Owner", r.Name);
        Assert.True(r.IsSystem);
        Assert.Equal(Seed, r.CreatedAt);
    }

    [Fact]
    public void RolePermission_Links_Role_And_Permission()
    {
        var rp = RolePermission.Create(RoleId, Permission.SecretsWrite);
        Assert.Equal(RoleId, rp.RoleId);
        Assert.Equal(Permission.SecretsWrite, rp.Permission);
    }

    [Fact]
    public void RoleAssignment_Stores_Scope_As_Columns()
    {
        var a = RoleAssignment.Create(Tenant, Principal, RoleId, Scope.Project(Proj));
        Assert.Equal(Principal, a.PrincipalId);
        Assert.Equal(RoleId, a.RoleId);
        Assert.Equal(ScopeType.Project, a.ScopeType);
        Assert.Equal(Proj, a.ScopeTargetId);
        Assert.Equal(Scope.Project(Proj), a.Scope);
    }

    [Fact]
    public void PermissionGrant_TenantScope_Has_Null_Target()
    {
        var g = PermissionGrant.Create(Tenant, Principal, Permission.AuditRead, Scope.Tenant);
        Assert.Equal(Permission.AuditRead, g.Permission);
        Assert.Equal(ScopeType.Tenant, g.ScopeType);
        Assert.Null(g.ScopeTargetId);
        Assert.Equal(Scope.Tenant, g.Scope);
    }

    [Fact]
    public void Invitation_Lifecycle()
    {
        var inv = Invitation.Create(Tenant, "new@example.com", RoleId, Scope.Tenant, "tok-123", Seed.AddDays(7));
        Assert.Equal(InvitationStatus.Pending, inv.Status);
        Assert.Equal("new@example.com", inv.Email);

        inv.Accept();
        Assert.Equal(InvitationStatus.Accepted, inv.Status);

        var inv2 = Invitation.Create(Tenant, "x@example.com", RoleId, Scope.Tenant, "tok-9", Seed.AddDays(7));
        inv2.Revoke();
        Assert.Equal(InvitationStatus.Revoked, inv2.Status);
    }

    [Fact]
    public void Invitation_Requires_Email_And_Token()
    {
        Assert.Throws<ArgumentException>(() => Invitation.Create(Tenant, " ", RoleId, Scope.Tenant, "t", Seed));
        Assert.Throws<ArgumentException>(() => Invitation.Create(Tenant, "e@x.com", RoleId, Scope.Tenant, " ", Seed));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~GrantModelTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Create `Role` and `RolePermission`**

`src/DeveloperPlatform.Domain/Authorization/Role.cs`:

```csharp
using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Authorization;

// A named bundle of permissions. v1 ships system roles only (IsSystem = true), which are global
// (not tenant-scoped) and seeded. Tenant-custom roles are a later slice.
public class Role : IEntity
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public string Name { get; private set; } = string.Empty;
    public bool IsSystem { get; private set; }

    private Role() { }

    // Explicit id + createdAt so system roles can be seeded deterministically via HasData.
    public static Role CreateSystem(Guid id, string name, DateTime createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Role { Id = id, Name = name, IsSystem = true, CreatedAt = createdAt };
    }
}
```

`src/DeveloperPlatform.Domain/Authorization/RolePermission.cs`:

```csharp
namespace DeveloperPlatform.Domain.Authorization;

// Join row: a permission belonging to a role. Composite key (RoleId, Permission).
public class RolePermission
{
    public Guid RoleId { get; private set; }
    public Permission Permission { get; private set; }

    private RolePermission() { }

    public static RolePermission Create(Guid roleId, Permission permission) =>
        new() { RoleId = roleId, Permission = permission };
}
```

- [ ] **Step 4: Create `RoleAssignment`, `PermissionGrant`, `Invitation`**

`src/DeveloperPlatform.Domain/Authorization/RoleAssignment.cs`:

```csharp
using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Authorization;

// Assigns a role to a principal at a scope. Scope persists as two columns; Scope value object is derived.
public class RoleAssignment : TenantEntity
{
    public Guid PrincipalId { get; private set; }
    public Guid RoleId { get; private set; }
    public ScopeType ScopeType { get; private set; }
    public Guid? ScopeTargetId { get; private set; }

    public Scope Scope => Scope.Create(ScopeType, ScopeTargetId);

    private RoleAssignment() { }

    public static RoleAssignment Create(Guid tenantId, Guid principalId, Guid roleId, Scope scope) =>
        new()
        {
            TenantId = tenantId,
            PrincipalId = principalId,
            RoleId = roleId,
            ScopeType = scope.Type,
            ScopeTargetId = scope.TargetId
        };
}
```

`src/DeveloperPlatform.Domain/Authorization/PermissionGrant.cs`:

```csharp
using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Authorization;

// A direct (ACL) grant of a single permission to a principal at a scope, outside any role.
public class PermissionGrant : TenantEntity
{
    public Guid PrincipalId { get; private set; }
    public Permission Permission { get; private set; }
    public ScopeType ScopeType { get; private set; }
    public Guid? ScopeTargetId { get; private set; }

    public Scope Scope => Scope.Create(ScopeType, ScopeTargetId);

    private PermissionGrant() { }

    public static PermissionGrant Create(Guid tenantId, Guid principalId, Permission permission, Scope scope) =>
        new()
        {
            TenantId = tenantId,
            PrincipalId = principalId,
            Permission = permission,
            ScopeType = scope.Type,
            ScopeTargetId = scope.TargetId
        };
}
```

`src/DeveloperPlatform.Domain/Authorization/Invitation.cs`:

```csharp
using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Authorization;

// A pending invite to join a tenant with a role at a scope. Activated on the invitee's first login (Slice 5).
public class Invitation : TenantEntity
{
    public string Email { get; private set; } = string.Empty;
    public Guid RoleId { get; private set; }
    public ScopeType ScopeType { get; private set; }
    public Guid? ScopeTargetId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public InvitationStatus Status { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    public Scope Scope => Scope.Create(ScopeType, ScopeTargetId);

    private Invitation() { }

    public static Invitation Create(
        Guid tenantId, string email, Guid roleId, Scope scope, string token, DateTime expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new Invitation
        {
            TenantId = tenantId,
            Email = email,
            RoleId = roleId,
            ScopeType = scope.Type,
            ScopeTargetId = scope.TargetId,
            Token = token,
            Status = InvitationStatus.Pending,
            ExpiresAt = expiresAt
        };
    }

    public void Accept() => Status = InvitationStatus.Accepted;
    public void Revoke() => Status = InvitationStatus.Revoked;
    public void MarkExpired() => Status = InvitationStatus.Expired;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~GrantModelTests"`
Expected: PASS (6 tests).

- [ ] **Step 6: Commit**

```bash
git add src/DeveloperPlatform.Domain/Authorization tests/DeveloperPlatform.Api.Tests/Authorization/GrantModelTests.cs
git commit -m "feat(authz): Role, RolePermission, RoleAssignment, PermissionGrant, Invitation entities"
```

---

## Task 4: EF configurations + DbSets + migration

**Files:**
- Create: `src/DeveloperPlatform.Infrastructure/Persistence/Converters/PermissionTokenConverter.cs`
- Create: the 9 configuration files listed in File Structure
- Modify: `src/DeveloperPlatform.Infrastructure/Persistence/ApplicationDbContext.cs` (add DbSets)
- Create (generated): `src/DeveloperPlatform.Infrastructure/Migrations/<timestamp>_AddAuthorizationModel.cs`

**Interfaces:**
- Consumes: all Task 2/3 entities; `PermissionCatalog.ToToken`/`FromToken` (Slice 1).
- Produces: DbSets `Users`, `Principals`, `Memberships`, `ServiceAccounts`, `Roles`, `RolePermissions`, `RoleAssignments`, `PermissionGrants`, `Invitations`; a reusable `PermissionTokenConverter`.

Note: configs are registered automatically via `modelBuilder.ApplyConfigurationsFromAssembly(...)` (already in `OnModelCreating`). This task has no unit test of its own — Task 5's persistence round-trip exercises the mappings; here the gate is "migration generates cleanly and the solution builds."

- [ ] **Step 1: Create the Permission value converter**

`src/DeveloperPlatform.Infrastructure/Persistence/Converters/PermissionTokenConverter.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DeveloperPlatform.Infrastructure.Persistence.Converters;

// Persists a Permission as its canonical resource:action token (single source of truth = PermissionCatalog).
public sealed class PermissionTokenConverter : ValueConverter<Permission, string>
{
    public PermissionTokenConverter()
        : base(p => PermissionCatalog.ToToken(p), s => PermissionCatalog.FromToken(s))
    {
    }
}
```

- [ ] **Step 2: Add DbSets to `ApplicationDbContext`**

In `src/DeveloperPlatform.Infrastructure/Persistence/ApplicationDbContext.cs`, add these `using`s at the top (next to the existing domain usings):

```csharp
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;
```

And add these DbSet properties after the existing `AuditEvents` DbSet (around line 26):

```csharp
    public DbSet<User> Users => Set<User>();
    public DbSet<Principal> Principals => Set<Principal>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<ServiceAccount> ServiceAccounts => Set<ServiceAccount>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<PermissionGrant> PermissionGrants => Set<PermissionGrant>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
```

- [ ] **Step 3: Create the configuration files**

`.../Configurations/UserConfiguration.cs`:

```csharp
using DeveloperPlatform.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.KeycloakSubject).HasMaxLength(255).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        builder.HasIndex(u => u.KeycloakSubject).IsUnique();
    }
}
```

`.../Configurations/PrincipalConfiguration.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class PrincipalConfiguration : IEntityTypeConfiguration<Principal>
{
    public void Configure(EntityTypeBuilder<Principal> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(p => p.TenantId);
    }
}
```

`.../Configurations/MembershipConfiguration.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(m => m.TenantId);
        builder.HasIndex(m => m.PrincipalId).IsUnique();

        builder.HasOne<Principal>().WithOne().HasForeignKey<Membership>(m => m.PrincipalId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

`.../Configurations/ServiceAccountConfiguration.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class ServiceAccountConfiguration : IEntityTypeConfiguration<ServiceAccount>
{
    public void Configure(EntityTypeBuilder<ServiceAccount> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(500);
        builder.HasIndex(s => s.TenantId);
        builder.HasIndex(s => s.PrincipalId).IsUnique();

        builder.HasOne<Principal>().WithOne().HasForeignKey<ServiceAccount>(s => s.PrincipalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

`.../Configurations/RoleConfiguration.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();
    }
}
```

`.../Configurations/RolePermissionConfiguration.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.HasKey(rp => new { rp.RoleId, rp.Permission });
        builder.Property(rp => rp.Permission)
            .HasConversion(new PermissionTokenConverter()).HasMaxLength(100);

        builder.HasOne<Role>().WithMany().HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

`.../Configurations/RoleAssignmentConfiguration.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ScopeType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Ignore(a => a.Scope);
        builder.HasIndex(a => a.TenantId);
        builder.HasIndex(a => a.PrincipalId);

        builder.HasOne<Principal>().WithMany().HasForeignKey(a => a.PrincipalId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Role>().WithMany().HasForeignKey(a => a.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

`.../Configurations/PermissionGrantConfiguration.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class PermissionGrantConfiguration : IEntityTypeConfiguration<PermissionGrant>
{
    public void Configure(EntityTypeBuilder<PermissionGrant> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Permission)
            .HasConversion(new PermissionTokenConverter()).HasMaxLength(100).IsRequired();
        builder.Property(g => g.ScopeType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Ignore(g => g.Scope);
        builder.HasIndex(g => g.TenantId);
        builder.HasIndex(g => g.PrincipalId);

        builder.HasOne<Principal>().WithMany().HasForeignKey(g => g.PrincipalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

`.../Configurations/InvitationConfiguration.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Email).HasMaxLength(320).IsRequired();
        builder.Property(i => i.Token).HasMaxLength(128).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.ScopeType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Ignore(i => i.Scope);
        builder.HasIndex(i => i.TenantId);
        builder.HasIndex(i => i.Token).IsUnique();

        builder.HasOne<Role>().WithMany().HasForeignKey(i => i.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 4: Verify the solution builds**

Run: `dotnet build developer-platform-reference.slnx --no-restore`
Expected: `Build succeeded. 0 Error(s)`. (Fix any config/entity mismatch before generating the migration.)

- [ ] **Step 5: Start MySQL for the design-time factory**

The design-time factory calls `ServerVersion.AutoDetect(connectionString)`, which opens a real connection. Ensure the DB is up:

Run: `docker compose up -d db`
Then wait until healthy:
Run: `docker compose ps db`
Expected: `db` shows `(healthy)`.

- [ ] **Step 6: Generate the migration**

Run: `dotnet ef migrations add AddAuthorizationModel --project src/DeveloperPlatform.Infrastructure --startup-project src/DeveloperPlatform.Api`
Expected: `Done.` A new `Migrations/<timestamp>_AddAuthorizationModel.cs` is created.

Verify the generated migration contains `Users`, `Principals`, `Memberships`, `ServiceAccounts`, `Roles`, `RolePermissions`, `RoleAssignments`, `PermissionGrants`, `Invitations` tables:
Run: `grep -oE 'CreateTable\(\s*name: "[^"]+"' src/DeveloperPlatform.Infrastructure/Migrations/*_AddAuthorizationModel.cs`
Expected: all nine table names listed.

- [ ] **Step 7: Build again to confirm the generated migration compiles**

Run: `dotnet build developer-platform-reference.slnx --no-restore`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add src/DeveloperPlatform.Infrastructure/Persistence src/DeveloperPlatform.Infrastructure/Migrations
git commit -m "feat(authz): EF configurations, DbSets, and AddAuthorizationModel migration"
```

---

## Task 5: Seed system roles + permissions

**Files:**
- Create: `src/DeveloperPlatform.Infrastructure/Authorization/SystemRoles.cs`
- Modify: `.../Configurations/RoleConfiguration.cs` and `.../Configurations/RolePermissionConfiguration.cs` (add `HasData`)
- Create (generated): a second migration `SeedSystemRoles`
- Test: `tests/DeveloperPlatform.Api.Tests/Authorization/AuthorizationPersistenceTests.cs`

**Interfaces:**
- Consumes: `Role`, `RolePermission`, `Permission` (Tasks 1-3); the EF model (Task 4).
- Produces: `SystemRoles` with fixed role Ids (`SystemRoles.OwnerId`, `AdminId`, `DeveloperId`, `ViewerId`) and `SystemRoles.All` (roles) + `SystemRoles.AllPermissions` (role-permission rows) for seeding and later reuse.

Built-in role → permission matrix (from spec §6; may be tuned):
- **Owner**: all 9 permissions.
- **Admin**: all except `RolesManage` (8).
- **Developer**: `ProjectsRead`, `ProjectsWrite`, `SecretsRead`, `SecretsWrite`.
- **Viewer**: `ProjectsRead`, `SecretsRead`, `AuditRead`.

- [ ] **Step 1: Write the failing persistence + seed test**

Create `tests/DeveloperPlatform.Api.Tests/Authorization/AuthorizationPersistenceTests.cs`:

```csharp
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;
using DeveloperPlatform.Infrastructure.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class AuthorizationPersistenceTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var ctx = new TestExecutionContext { TenantId = _tenantId };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task System_Roles_Are_Seeded_With_Their_Permissions()
    {
        var roles = await _db.Roles.AsNoTracking().ToListAsync();
        Assert.Equal(4, roles.Count);
        Assert.All(roles, r => Assert.True(r.IsSystem));

        var ownerPerms = await _db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == SystemRoles.OwnerId).ToListAsync();
        Assert.Equal(Enum.GetValues<Permission>().Length, ownerPerms.Count);

        var viewerPerms = await _db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == SystemRoles.ViewerId).Select(rp => rp.Permission).ToListAsync();
        Assert.Contains(Permission.ProjectsRead, viewerPerms);
        Assert.DoesNotContain(Permission.ProjectsWrite, viewerPerms);
    }

    [Fact]
    public async Task Member_Principal_And_Grant_RoundTrip()
    {
        var user = User.Create("kc-1", "dev@example.com", "Dev");
        _db.Users.Add(user);
        var principal = Principal.CreateMember(_tenantId, "Dev");
        _db.Principals.Add(principal);
        _db.Memberships.Add(Membership.Create(_tenantId, principal.Id, user.Id, MembershipStatus.Active));
        _db.PermissionGrants.Add(
            PermissionGrant.Create(_tenantId, principal.Id, Permission.SecretsWrite, Scope.Tenant));
        await _db.SaveChangesAsync();

        var grant = await _db.PermissionGrants.AsNoTracking().SingleAsync();
        Assert.Equal(principal.Id, grant.PrincipalId);
        Assert.Equal(Permission.SecretsWrite, grant.Permission);
        Assert.Equal(ScopeType.Tenant, grant.ScopeType);
    }

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
}
```

Note: this test references the CURRENT `IExecutionContext` shape (`UserId`/`ApiKeyId`). That interface is not changed until Slice 3 — do not change it here.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~AuthorizationPersistenceTests"`
Expected: FAIL — `SystemRoles` does not exist / roles not seeded.

- [ ] **Step 3: Create `SystemRoles`**

`src/DeveloperPlatform.Infrastructure/Authorization/SystemRoles.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Infrastructure.Authorization;

// Deterministic definitions for the built-in system roles, used for HasData seeding and later reuse.
public static class SystemRoles
{
    // Fixed ids + a fixed timestamp so HasData seed data is stable across migrations.
    public static readonly Guid OwnerId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AdminId = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid DeveloperId = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid ViewerId = new("44444444-4444-4444-4444-444444444444");

    public static readonly DateTime SeededAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Permission[] AllPerms = Enum.GetValues<Permission>();

    private static readonly Permission[] AdminPerms =
        AllPerms.Where(p => p != Permission.RolesManage).ToArray();

    private static readonly Permission[] DeveloperPerms =
    [
        Permission.ProjectsRead, Permission.ProjectsWrite,
        Permission.SecretsRead, Permission.SecretsWrite,
    ];

    private static readonly Permission[] ViewerPerms =
    [
        Permission.ProjectsRead, Permission.SecretsRead, Permission.AuditRead,
    ];

    public static IReadOnlyList<Role> All { get; } =
    [
        Role.CreateSystem(OwnerId, "Owner", SeededAt),
        Role.CreateSystem(AdminId, "Admin", SeededAt),
        Role.CreateSystem(DeveloperId, "Developer", SeededAt),
        Role.CreateSystem(ViewerId, "Viewer", SeededAt),
    ];

    public static IReadOnlyList<RolePermission> AllPermissions { get; } =
    [
        .. AllPerms.Select(p => RolePermission.Create(OwnerId, p)),
        .. AdminPerms.Select(p => RolePermission.Create(AdminId, p)),
        .. DeveloperPerms.Select(p => RolePermission.Create(DeveloperId, p)),
        .. ViewerPerms.Select(p => RolePermission.Create(ViewerId, p)),
    ];
}
```

- [ ] **Step 4: Add `HasData` seeding to the role configs**

In `.../Configurations/RoleConfiguration.cs`, add a `using DeveloperPlatform.Infrastructure.Authorization;` and, at the end of `Configure`, seed the roles:

```csharp
        builder.HasData(SystemRoles.All);
```

In `.../Configurations/RolePermissionConfiguration.cs`, add a `using DeveloperPlatform.Infrastructure.Authorization;` and, at the end of `Configure`, seed the role-permission rows:

```csharp
        builder.HasData(SystemRoles.AllPermissions);
```

- [ ] **Step 5: Run the persistence test to verify it passes**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~AuthorizationPersistenceTests"`
Expected: PASS (2 tests). (InMemory applies `HasData` seed data on `EnsureCreated`.)

- [ ] **Step 6: Generate the seed migration for MySQL**

Ensure the DB is up (`docker compose up -d db`), then:
Run: `dotnet ef migrations add SeedSystemRoles --project src/DeveloperPlatform.Infrastructure --startup-project src/DeveloperPlatform.Api`
Expected: `Done.` The migration contains `InsertData` calls for `Roles` and `RolePermissions`.

Verify:
Run: `grep -c "InsertData" src/DeveloperPlatform.Infrastructure/Migrations/*_SeedSystemRoles.cs`
Expected: a count ≥ 2.

- [ ] **Step 7: Build to confirm everything compiles**

Run: `dotnet build developer-platform-reference.slnx --no-restore`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add src/DeveloperPlatform.Infrastructure tests/DeveloperPlatform.Api.Tests/Authorization/AuthorizationPersistenceTests.cs
git commit -m "feat(authz): seed built-in system roles and their permissions"
```

---

## Task 6: Update architecture tests for global entities

**Files:**
- Modify: `tests/DeveloperPlatform.ArchitectureTests/DomainLayerTests.cs`

**Interfaces:**
- Consumes: `User`, `Role` (Tasks 2-3).
- Produces: nothing (test-only).

The rule `All_Concrete_Domain_Entities_Implement_ITenantScoped` currently excludes only `Tenant`/`TenantEncryptionKey`. `User` and `Role` are new global `IEntity` types that are intentionally NOT tenant-scoped, so they must be excluded too, or the rule fails.

- [ ] **Step 1: Run the architecture tests to observe the new failure**

Run: `dotnet test tests/DeveloperPlatform.ArchitectureTests`
Expected: FAIL — `All_Concrete_Domain_Entities_Implement_ITenantScoped` reports `User, Role` as failing types.

- [ ] **Step 2: Add the exclusions**

In `tests/DeveloperPlatform.ArchitectureTests/DomainLayerTests.cs`, extend the filter in `All_Concrete_Domain_Entities_Implement_ITenantScoped`. Change:

```csharp
            .And().DoNotHaveNameMatching("Tenant$")
            .And().DoNotHaveNameMatching("TenantEncryptionKey")
```

to:

```csharp
            .And().DoNotHaveNameMatching("Tenant$")
            .And().DoNotHaveNameMatching("TenantEncryptionKey")
            .And().DoNotHaveNameMatching("^User$")
            .And().DoNotHaveNameMatching("^Role$")
```

- [ ] **Step 3: Run the architecture tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.ArchitectureTests`
Expected: PASS (10 tests). If `Role` still matches (the pattern is a regex over full names), confirm `^Role$` excludes only `Role` and not `RolePermission`/`RoleAssignment` — those are correctly tenant-scoped or non-IEntity and should NOT be excluded.

- [ ] **Step 4: Run the full authorization test set**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Authorization"`
Expected: PASS — all Slice 1 + Slice 2 authorization tests green (the pre-existing `Projects`/`Auth` integration tests are not matched by this filter's `Authorization` namespace... note they share the substring; if they run, the 5 need RabbitMQ up — see note).

Note: the `~Authorization` filter also matches `ProjectsAuthorizationTests`/`ApiAuthorizationTests` by class name. To run only this slice's unit tests, use:
`dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Api.Tests.Authorization"`

- [ ] **Step 5: Commit**

```bash
git add tests/DeveloperPlatform.ArchitectureTests/DomainLayerTests.cs
git commit -m "test(authz): exclude global User and Role entities from tenant-scoped rule"
```

---

## Self-Review

**1. Spec coverage (Slice 2 scope):**
- Spec §3 entities — `User`, `Principal`, `Membership`, `ServiceAccount`, `Role`, `RolePermission`, `RoleAssignment`, `PermissionGrant`, `Invitation`, `Scope` — all created (Tasks 1-3). ✅ (`ApiKeyCredential` is Slice 4, not here.)
- Spec §3 "User is global (IEntity)... everything else TenantEntity" — `User`/`Role` global, rest tenant-scoped (Tasks 2-3, arch test Task 6). ✅
- Spec §4 EF `HasConversion` of the permission token — `PermissionTokenConverter` (Task 4). ✅
- Spec §6 "system roles seeded (Owner/Admin/Developer/Viewer)" — Task 5. ✅
- Spec §10 "new tables ... migration" — Task 4/5 migrations. ✅ (Cut-over/replacement of the old `ApiKey` table is Slice 4, where the credential model lands.)
- Enforcement, `IAuthorizationService`, `IExecutionContext`/audit changes — explicitly Slice 3, out of scope. ✅

**2. Placeholder scan:** No `TBD`/`TODO`/vague steps. Every code step is complete. The two migration files are generated by tooling (not hand-transcribed) with explicit verification greps. ✅

**3. Type consistency:** `Principal.Create{Member,ServiceAccount}`, `Membership.Create(tenantId, principalId, userId, status)`, `ServiceAccount.Create(tenantId, principalId, name, description)`, `Role.CreateSystem(id, name, createdAt)`, `RolePermission.Create(roleId, permission)`, `RoleAssignment.Create(tenantId, principalId, roleId, scope)`, `PermissionGrant.Create(tenantId, principalId, permission, scope)`, `Invitation.Create(tenantId, email, roleId, scope, token, expiresAt)`, `Scope.{Tenant,Project,Environment,Create,Encompasses}`, `PermissionTokenConverter`, `SystemRoles.{OwnerId,AdminId,DeveloperId,ViewerId,All,AllPermissions}` — used identically across entities, configs, seeds, and tests. `.Ignore(x => x.Scope)` in the three configs matches the derived `Scope` property on `RoleAssignment`/`PermissionGrant`/`Invitation`. ✅

**4. Known environmental notes:** migration generation needs `docker compose up -d db` (design-time factory auto-detects server version by connecting); the seed round-trip test uses EF InMemory and needs no infra.
