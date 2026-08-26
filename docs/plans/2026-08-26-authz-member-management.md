# Member Management & Onboarding Implementation Plan (Authz Slice 5 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the authorization model manageable and secure: a privilege-escalation guard, grant-management endpoints (assign/revoke roles & permissions), member invitations with invitation-gated onboarding, plus the critical fixes the e2e surfaced (tenant-key provisioning, string-enum binding, membership uniqueness).

**Architecture:** A `PrivilegeGuard` enforces "you can only grant/assign what you hold" (via the Slice 3 `IAuthorizationService`), called inside every grant-issuing handler. Grant/role commands mutate `RoleAssignment`/`PermissionGrant`; invitations create `Invitation` rows. The reworked `PrincipalResolver` gates membership: first user → Owner (and provisions the tenant encryption key), an invited user → the invited role (invitation marked Accepted), everyone else → no membership (→ 403). A global `JsonStringEnumConverter` makes `"ProjectsRead"`/`"Tenant"` bind and bad enums 400.

**Tech Stack:** .NET 10, EF Core + MySQL, ASP.NET Core, xUnit + EF InMemory.

## Where this fits

**Slice 5 of 6.** Spec: `docs/specs/2026-08-25-permissions-member-management-design.md` (§6 escalation guard, §7 members/grants endpoints, §5 onboarding). Depends on Slices 1-4 (merged).

1. Catalog ✅ 2. Model+seed ✅ 3. Enforcement ✅ 4. API keys ✅ 5. **Member management** ← this plan 6. Web Access UI

**Deferred:** the composite `(PrincipalId, TenantId)` FK hardening; custom (non-system) roles; per-key downscoping. `localhost`→`127.0.0.1` appsettings cleanup is a trivial config change included in Task 5.

## Global Constraints

- `net10.0`; `Nullable` + `ImplicitUsings`. No type named `Action`.
- Layering: contracts/attributes in Application; handlers/services in Infrastructure; entities in Domain. Domain has no outward deps.
- **Escalation rule:** to grant a permission you must hold it at that scope; to assign/invite-to a role you must hold ALL that role's permissions at that scope. Enforced by `PrivilegeGuard` inside the handler (the coarse `[RequiresPermission]` gate is separate). The actor is `executionContext.PrincipalId`.
- **Onboarding (invitation-gated):** first member of a tenant → Owner + provision `TenantEncryptionKey`; a caller with a matching pending `Invitation` → the invited role (+ mark Accepted); otherwise → no membership (resolver returns null → guarded ops 403).
- **Build is `-warnaserror`** (no unused usings). `.gitattributes` mandates **CRLF** for `*.cs`; strip BOM on generated files. `commit-msg` hook REJECTS AI co-author trailers. Never `--no-verify`.
- **Migrations need MySQL:** `docker compose up -d db`; `dotnet ef` with `--project src/DeveloperPlatform.Infrastructure --startup-project src/DeveloperPlatform.Infrastructure` and a `127.0.0.1` connection string.
- Test framework xUnit; new tests under `tests/DeveloperPlatform.Api.Tests/Authorization/`.

---

## File Structure

**Application:** `Authorization/IPrivilegeGuard.cs`; `Grants/{AssignRole,GrantPermission,RevokeRoleAssignment,RevokePermissionGrant}/*Command.cs`; `Grants/GetRoles/GetRolesQuery.cs`; `Members/GetMembers/GetMembersQuery.cs`; `Members/InviteMember/InviteMemberCommand.cs`; `Members/RevokeInvitation/RevokeInvitationCommand.cs`; `Members/GetInvitations/GetInvitationsQuery.cs`.
**Infrastructure:** `Authorization/PrivilegeGuard.cs`; matching handlers under `Infrastructure/Members/`; modify `PrincipalResolver.cs`, `ServiceCollectionExtensions.cs`, `IssueApiKeyCommandHandler`? no; `CreateServiceAccountCommandHandler.cs` (add guard); a `Configurations` change + migration.
**Api:** `Endpoints/Members/MembersEndpoints.cs` (members, invitations, roles); `Endpoints/Principals/PrincipalGrantsEndpoints.cs` (role-assignments, permission-grants); modify `Program.cs` (register endpoints + `JsonStringEnumConverter`).
**Tests:** `PrivilegeGuardTests.cs`, `GrantManagementTests.cs`, `InvitationTests.cs`, and updates to `PrincipalResolverTests.cs`.

---

## Task 1: Privilege-escalation guard

**Files:** Create `src/DeveloperPlatform.Application/Authorization/IPrivilegeGuard.cs`, `src/DeveloperPlatform.Infrastructure/Authorization/PrivilegeGuard.cs`; Test `tests/DeveloperPlatform.Api.Tests/Authorization/PrivilegeGuardTests.cs`.

**Interfaces:**
- Consumes: `IAuthorizationService`, `ForbiddenException` (Slice 3); `ApplicationDbContext`, `RolePermission` (Slice 2); `Permission`, `Scope`.
- Produces: `interface IPrivilegeGuard { Task EnsureCanGrantAsync(Guid actorPrincipalId, Permission permission, Scope scope, CancellationToken ct = default); Task EnsureCanAssignRoleAsync(Guid actorPrincipalId, Guid roleId, Scope scope, CancellationToken ct = default); }`

- [ ] **Step 1: Write the failing tests**

Create `tests/DeveloperPlatform.Api.Tests/Authorization/PrivilegeGuardTests.cs`:

```csharp
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class PrivilegeGuardTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private PrivilegeGuard _sut = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var ctx = new TestExecutionContext { TenantId = _tenant };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
        _sut = new PrivilegeGuard(new AuthorizationService(_db), _db);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task EnsureCanGrant_Allows_When_Actor_Holds_It()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _actor, Permission.SecretsWrite, Scope.Tenant));
        await _db.SaveChangesAsync();
        await _sut.EnsureCanGrantAsync(_actor, Permission.SecretsWrite, Scope.Tenant);  // does not throw
    }

    [Fact]
    public async Task EnsureCanGrant_Throws_When_Actor_Lacks_It()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.EnsureCanGrantAsync(_actor, Permission.SecretsWrite, Scope.Tenant));
    }

    [Fact]
    public async Task EnsureCanAssignRole_Requires_All_Role_Permissions()
    {
        var roleId = Guid.NewGuid();
        _db.Roles.Add(Role.CreateSystem(roleId, "TwoPerm", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        _db.RolePermissions.Add(RolePermission.Create(roleId, Permission.ProjectsRead));
        _db.RolePermissions.Add(RolePermission.Create(roleId, Permission.SecretsWrite));
        // actor holds only one of the two
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _actor, Permission.ProjectsRead, Scope.Tenant));
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.EnsureCanAssignRoleAsync(_actor, roleId, Scope.Tenant));

        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _actor, Permission.SecretsWrite, Scope.Tenant));
        await _db.SaveChangesAsync();
        await _sut.EnsureCanAssignRoleAsync(_actor, roleId, Scope.Tenant);  // now holds both → ok
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        public Guid TenantId { get; set; }
        public Guid? PrincipalId => null;
        public PrincipalType? PrincipalType => null;
        public Guid? UserId => null;
        public Guid? ProjectId => null;
        public Guid? EnvironmentId => null;
        public string IpAddress => "127.0.0.1";
        public bool IsCrossTenantOperation { get; set; }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~PrivilegeGuardTests"` → FAIL (types missing).

- [ ] **Step 3: Create the interface**

`src/DeveloperPlatform.Application/Authorization/IPrivilegeGuard.cs`:

```csharp
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Authorization;

// Prevents privilege escalation: an actor may only grant/assign what it itself holds.
public interface IPrivilegeGuard
{
    // Throws ForbiddenException unless the actor holds `permission` at `scope`.
    Task EnsureCanGrantAsync(Guid actorPrincipalId, Permission permission, Scope scope, CancellationToken ct = default);

    // Throws ForbiddenException unless the actor holds EVERY permission of `roleId` at `scope`.
    Task EnsureCanAssignRoleAsync(Guid actorPrincipalId, Guid roleId, Scope scope, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement `PrivilegeGuard`**

`src/DeveloperPlatform.Infrastructure/Authorization/PrivilegeGuard.cs`:

```csharp
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Authorization;

public sealed class PrivilegeGuard(IAuthorizationService authorizationService, ApplicationDbContext db)
    : IPrivilegeGuard
{
    public async Task EnsureCanGrantAsync(
        Guid actorPrincipalId, Permission permission, Scope scope, CancellationToken ct = default)
    {
        if (!await authorizationService.IsAuthorizedAsync(actorPrincipalId, permission, scope, ct))
        {
            throw new ForbiddenException(
                $"Cannot grant '{PermissionCatalog.ToToken(permission)}' — the actor does not hold it at {scope.Type}.");
        }
    }

    public async Task EnsureCanAssignRoleAsync(
        Guid actorPrincipalId, Guid roleId, Scope scope, CancellationToken ct = default)
    {
        var rolePermissions = await db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission)
            .ToListAsync(ct);

        foreach (var permission in rolePermissions)
        {
            await EnsureCanGrantAsync(actorPrincipalId, permission, scope, ct);
        }
    }
}
```

- [ ] **Step 5: Run to verify pass, then commit**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~PrivilegeGuardTests"` → PASS (3). Then build.

```bash
git add src/DeveloperPlatform.Application/Authorization/IPrivilegeGuard.cs src/DeveloperPlatform.Infrastructure/Authorization/PrivilegeGuard.cs tests/DeveloperPlatform.Api.Tests/Authorization/PrivilegeGuardTests.cs
git commit -m "feat(authz): privilege-escalation guard (grant only what you hold)"
```

---

## Task 2: Grant-management commands, queries + endpoints

**Files:** Application: `Grants/AssignRole/AssignRoleCommand.cs`, `Grants/GrantPermission/GrantPermissionCommand.cs`, `Grants/RevokeRoleAssignment/RevokeRoleAssignmentCommand.cs`, `Grants/RevokePermissionGrant/RevokePermissionGrantCommand.cs`, `Grants/GetRoles/GetRolesQuery.cs`, `Members/GetMembers/GetMembersQuery.cs`. Infrastructure: matching handlers under `Infrastructure/Members/`. Api: `Endpoints/Principals/PrincipalGrantsEndpoints.cs` + roles/members in `Endpoints/Members/MembersEndpoints.cs`. Modify `ServiceCollectionExtensions.cs`, `Program.cs`. Test: `GrantManagementTests.cs`.

**Interfaces produced:**
- `AssignRoleCommand(Guid PrincipalId, Guid RoleId, ScopeType ScopeType, Guid? ScopeTargetId) : ICommand<AssignRoleResult>` `[RequiresPermission(RolesManage)]` → `AssignRoleResult(Guid AssignmentId)`.
- `GrantPermissionCommand(Guid PrincipalId, Permission Permission, ScopeType ScopeType, Guid? ScopeTargetId) : ICommand<GrantPermissionResult>` `[RequiresPermission(RolesManage)]` → `GrantPermissionResult(Guid GrantId)`.
- `RevokeRoleAssignmentCommand(Guid AssignmentId) : ICommand` `[RolesManage]`; `RevokePermissionGrantCommand(Guid GrantId) : ICommand` `[RolesManage]`.
- `GetRolesQuery : IQuery<IReadOnlyList<RoleSummary>>` `[RolesManage]` → `RoleSummary(Guid Id, string Name, IReadOnlyList<string> Permissions)`.
- `GetMembersQuery : IQuery<IReadOnlyList<MemberSummary>>` `[MembersManage]` → `MemberSummary(Guid PrincipalId, Guid UserId, string Email, string DisplayName, string Status)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DeveloperPlatform.Api.Tests/Authorization/GrantManagementTests.cs`:

```csharp
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Grants.AssignRole;
using DeveloperPlatform.Application.Grants.GrantPermission;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Authorization;
using DeveloperPlatform.Infrastructure.Members;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class GrantManagementTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();   // the caller (execution context principal)
    private readonly Guid _target = Guid.NewGuid();   // the principal being granted to

    public async Task InitializeAsync()
    {
        var ctx = new Ctx { TenantId = _tenant, PrincipalId = _actor };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
        _ctx = ctx;
    }
    private Ctx _ctx = null!;
    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private GrantPermissionCommandHandler GrantHandler() =>
        new(_db, _ctx, new PrivilegeGuard(new AuthorizationService(_db), _db));

    [Fact]
    public async Task GrantPermission_Denied_When_Actor_Lacks_It()
    {
        await Assert.ThrowsAsync<DeveloperPlatform.Application.Authorization.ForbiddenException>(
            () => GrantHandler().HandleAsync(
                new GrantPermissionCommand(_target, Permission.SecretsWrite, ScopeType.Tenant, null)));
    }

    [Fact]
    public async Task GrantPermission_Succeeds_And_Persists_When_Actor_Holds_It()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _actor, Permission.SecretsWrite, Scope.Tenant));
        await _db.SaveChangesAsync();

        var result = await GrantHandler().HandleAsync(
            new GrantPermissionCommand(_target, Permission.SecretsWrite, ScopeType.Tenant, null));

        var grant = await _db.PermissionGrants.AsNoTracking().SingleAsync(g => g.Id == result.GrantId);
        Assert.Equal(_target, grant.PrincipalId);
        Assert.Equal(Permission.SecretsWrite, grant.Permission);
    }

    private sealed class Ctx : IExecutionContext
    {
        public Guid TenantId { get; set; }
        public Guid? PrincipalId { get; set; }
        public PrincipalType? PrincipalType => Domain.Authorization.PrincipalType.Member;
        public Guid? UserId => null;
        public Guid? ProjectId => null;
        public Guid? EnvironmentId => null;
        public string IpAddress => "127.0.0.1";
        public bool IsCrossTenantOperation { get; set; }
    }
}
```

(Note: `Domain.Authorization.PrincipalType` may need full qualification — if the `PrincipalType` property/type collide, fully-qualify as `DeveloperPlatform.Domain.Authorization.PrincipalType`.)

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~GrantManagementTests"` → FAIL.

- [ ] **Step 3: Create the Application commands/queries**

`src/DeveloperPlatform.Application/Grants/AssignRole/AssignRoleCommand.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Grants.AssignRole;

[RequiresPermission(Permission.RolesManage)]
public record AssignRoleCommand(Guid PrincipalId, Guid RoleId, ScopeType ScopeType, Guid? ScopeTargetId)
    : ICommand<AssignRoleResult>;

public record AssignRoleResult(Guid AssignmentId);
```

`src/DeveloperPlatform.Application/Grants/GrantPermission/GrantPermissionCommand.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Grants.GrantPermission;

[RequiresPermission(Permission.RolesManage)]
public record GrantPermissionCommand(Guid PrincipalId, Permission Permission, ScopeType ScopeType, Guid? ScopeTargetId)
    : ICommand<GrantPermissionResult>;

public record GrantPermissionResult(Guid GrantId);
```

`src/DeveloperPlatform.Application/Grants/RevokeRoleAssignment/RevokeRoleAssignmentCommand.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Grants.RevokeRoleAssignment;

[RequiresPermission(Permission.RolesManage)]
public record RevokeRoleAssignmentCommand(Guid AssignmentId) : ICommand;
```

`src/DeveloperPlatform.Application/Grants/RevokePermissionGrant/RevokePermissionGrantCommand.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Grants.RevokePermissionGrant;

[RequiresPermission(Permission.RolesManage)]
public record RevokePermissionGrantCommand(Guid GrantId) : ICommand;
```

`src/DeveloperPlatform.Application/Grants/GetRoles/GetRolesQuery.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Grants.GetRoles;

[RequiresPermission(Permission.RolesManage)]
public record GetRolesQuery : IQuery<IReadOnlyList<RoleSummary>>;

public record RoleSummary(Guid Id, string Name, IReadOnlyList<string> Permissions);
```

`src/DeveloperPlatform.Application/Members/GetMembers/GetMembersQuery.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Members.GetMembers;

[RequiresPermission(Permission.MembersManage)]
public record GetMembersQuery : IQuery<IReadOnlyList<MemberSummary>>;

public record MemberSummary(Guid PrincipalId, Guid UserId, string Email, string DisplayName, string Status);
```

- [ ] **Step 4: Create the Infrastructure handlers**

`src/DeveloperPlatform.Infrastructure/Members/GrantPermissionCommandHandler.cs`:

```csharp
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Grants.GrantPermission;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class GrantPermissionCommandHandler(
    ApplicationDbContext db, IExecutionContext executionContext, IPrivilegeGuard guard)
    : ICommandHandler<GrantPermissionCommand, GrantPermissionResult>
{
    public async Task<GrantPermissionResult> HandleAsync(GrantPermissionCommand command, CancellationToken ct = default)
    {
        var scope = Scope.Create(command.ScopeType, command.ScopeTargetId);
        var actor = executionContext.PrincipalId
            ?? throw new ForbiddenException("No acting principal.");
        await guard.EnsureCanGrantAsync(actor, command.Permission, scope, ct);

        var grant = PermissionGrant.Create(executionContext.TenantId, command.PrincipalId, command.Permission, scope);
        db.PermissionGrants.Add(grant);
        await db.SaveChangesAsync(ct);
        return new GrantPermissionResult(grant.Id);
    }
}
```

`src/DeveloperPlatform.Infrastructure/Members/AssignRoleCommandHandler.cs`:

```csharp
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Grants.AssignRole;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class AssignRoleCommandHandler(
    ApplicationDbContext db, IExecutionContext executionContext, IPrivilegeGuard guard)
    : ICommandHandler<AssignRoleCommand, AssignRoleResult>
{
    public async Task<AssignRoleResult> HandleAsync(AssignRoleCommand command, CancellationToken ct = default)
    {
        var scope = Scope.Create(command.ScopeType, command.ScopeTargetId);
        var actor = executionContext.PrincipalId
            ?? throw new ForbiddenException("No acting principal.");
        await guard.EnsureCanAssignRoleAsync(actor, command.RoleId, scope, ct);

        var assignment = RoleAssignment.Create(executionContext.TenantId, command.PrincipalId, command.RoleId, scope);
        db.RoleAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);
        return new AssignRoleResult(assignment.Id);
    }
}
```

`src/DeveloperPlatform.Infrastructure/Members/RevokeRoleAssignmentCommandHandler.cs`:

```csharp
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Grants.RevokeRoleAssignment;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class RevokeRoleAssignmentCommandHandler(ApplicationDbContext db)
    : ICommandHandler<RevokeRoleAssignmentCommand, Unit>
{
    public async Task<Unit> HandleAsync(RevokeRoleAssignmentCommand command, CancellationToken ct = default)
    {
        var assignment = await db.RoleAssignments.FirstOrDefaultAsync(a => a.Id == command.AssignmentId, ct)
            ?? throw new KeyNotFoundException($"Role assignment {command.AssignmentId} not found.");
        db.RoleAssignments.Remove(assignment);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

`src/DeveloperPlatform.Infrastructure/Members/RevokePermissionGrantCommandHandler.cs`:

```csharp
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Grants.RevokePermissionGrant;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class RevokePermissionGrantCommandHandler(ApplicationDbContext db)
    : ICommandHandler<RevokePermissionGrantCommand, Unit>
{
    public async Task<Unit> HandleAsync(RevokePermissionGrantCommand command, CancellationToken ct = default)
    {
        var grant = await db.PermissionGrants.FirstOrDefaultAsync(g => g.Id == command.GrantId, ct)
            ?? throw new KeyNotFoundException($"Permission grant {command.GrantId} not found.");
        db.PermissionGrants.Remove(grant);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

`src/DeveloperPlatform.Infrastructure/Members/GetRolesQueryHandler.cs`:

```csharp
using DeveloperPlatform.Application.Grants.GetRoles;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class GetRolesQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetRolesQuery, IReadOnlyList<RoleSummary>>
{
    public async Task<IReadOnlyList<RoleSummary>> HandleAsync(GetRolesQuery query, CancellationToken ct = default)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        var perms = await db.RolePermissions.AsNoTracking().ToListAsync(ct);
        return roles.Select(r => new RoleSummary(
            r.Id, r.Name,
            perms.Where(p => p.RoleId == r.Id).Select(p => PermissionCatalog.ToToken(p.Permission)).OrderBy(t => t).ToList()))
            .ToList();
    }
}
```

`src/DeveloperPlatform.Infrastructure/Members/GetMembersQueryHandler.cs`:

```csharp
using DeveloperPlatform.Application.Members.GetMembers;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class GetMembersQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetMembersQuery, IReadOnlyList<MemberSummary>>
{
    public async Task<IReadOnlyList<MemberSummary>> HandleAsync(GetMembersQuery query, CancellationToken ct = default)
    {
        // Memberships are tenant-filtered; join the global Users table for identity.
        var rows = await db.Memberships.AsNoTracking()
            .Join(db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new { m, u })
            .Select(x => new MemberSummary(
                x.m.PrincipalId, x.u.Id, x.u.Email, x.u.DisplayName, x.m.Status.ToString()))
            .ToListAsync(ct);
        return rows;
    }
}
```

- [ ] **Step 5: Register the handlers**

In `ServiceCollectionExtensions.cs` add the `using`s and register the six handlers (mirroring the existing `AddScoped<ICommandHandler<...>, ...>()` lines):

```csharp
        services.AddScoped<ICommandHandler<AssignRoleCommand, AssignRoleResult>, AssignRoleCommandHandler>();
        services.AddScoped<ICommandHandler<GrantPermissionCommand, GrantPermissionResult>, GrantPermissionCommandHandler>();
        services.AddScoped<ICommandHandler<RevokeRoleAssignmentCommand, Unit>, RevokeRoleAssignmentCommandHandler>();
        services.AddScoped<ICommandHandler<RevokePermissionGrantCommand, Unit>, RevokePermissionGrantCommandHandler>();
        services.AddScoped<IQueryHandler<GetRolesQuery, IReadOnlyList<RoleSummary>>, GetRolesQueryHandler>();
        services.AddScoped<IQueryHandler<GetMembersQuery, IReadOnlyList<MemberSummary>>, GetMembersQueryHandler>();
        services.AddScoped<IPrivilegeGuard, PrivilegeGuard>();
```

(add the `using DeveloperPlatform.Application.Grants.*;`, `using DeveloperPlatform.Application.Members.GetMembers;`, `using DeveloperPlatform.Infrastructure.Members;`, `using DeveloperPlatform.Application.Authorization;` as needed.)

- [ ] **Step 6: Run the handler tests, then create the endpoints**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~GrantManagementTests"` → PASS (2).

`src/DeveloperPlatform.Api/Endpoints/Principals/PrincipalGrantsEndpoints.cs`:

```csharp
using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Grants.AssignRole;
using DeveloperPlatform.Application.Grants.GrantPermission;
using DeveloperPlatform.Application.Grants.RevokePermissionGrant;
using DeveloperPlatform.Application.Grants.RevokeRoleAssignment;
using DeveloperPlatform.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeveloperPlatform.Api.Endpoints.Principals;

public static class PrincipalGrantsEndpoints
{
    public static IEndpointRouteBuilder MapPrincipalGrants(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v1/principals/{principalId:guid}")
            .WithTags("Access Management").WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        group.MapPost("/role-assignments", async (
            Guid principalId, [FromBody] AssignRoleRequest req, ICommandDispatcher d, CancellationToken ct) =>
        {
            var r = await d.SendAsync<AssignRoleCommand, AssignRoleResult>(
                new AssignRoleCommand(principalId, req.RoleId, req.ScopeType, req.ScopeTargetId), ct);
            return Results.Created($"/api/v1/principals/{principalId}/role-assignments/{r.AssignmentId}", r);
        }).WithName("AssignRole").Produces<AssignRoleResult>(StatusCodes.Status201Created)
          .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/role-assignments/{assignmentId:guid}", async (
            Guid assignmentId, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<RevokeRoleAssignmentCommand, Unit>(new RevokeRoleAssignmentCommand(assignmentId), ct);
            return Results.NoContent();
        }).WithName("RevokeRoleAssignment").Produces(StatusCodes.Status204NoContent);

        group.MapPost("/permission-grants", async (
            Guid principalId, [FromBody] GrantPermissionRequest req, ICommandDispatcher d, CancellationToken ct) =>
        {
            var r = await d.SendAsync<GrantPermissionCommand, GrantPermissionResult>(
                new GrantPermissionCommand(principalId, req.Permission, req.ScopeType, req.ScopeTargetId), ct);
            return Results.Created($"/api/v1/principals/{principalId}/permission-grants/{r.GrantId}", r);
        }).WithName("GrantPermission").Produces<GrantPermissionResult>(StatusCodes.Status201Created)
          .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/permission-grants/{grantId:guid}", async (
            Guid grantId, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<RevokePermissionGrantCommand, Unit>(new RevokePermissionGrantCommand(grantId), ct);
            return Results.NoContent();
        }).WithName("RevokePermissionGrant").Produces(StatusCodes.Status204NoContent);

        return app;
    }

    public record AssignRoleRequest(Guid RoleId, ScopeType ScopeType, Guid? ScopeTargetId);
    public record GrantPermissionRequest(Permission Permission, ScopeType ScopeType, Guid? ScopeTargetId);
}
```

`src/DeveloperPlatform.Api/Endpoints/Members/MembersEndpoints.cs` (roles + members list; invitations added in Task 3):

```csharp
using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Grants.GetRoles;
using DeveloperPlatform.Application.Members.GetMembers;
using DeveloperPlatform.Application.Queries;

namespace DeveloperPlatform.Api.Endpoints.Members;

public static class MembersEndpoints
{
    public static IEndpointRouteBuilder MapMembers(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        app.MapGet("/api/v1/roles", async (IQueryDispatcher d, CancellationToken ct) =>
            Results.Ok(await d.SendAsync<GetRolesQuery, IReadOnlyList<RoleSummary>>(new GetRolesQuery(), ct)))
            .WithName("GetRoles").WithTags("Access Management")
            .Produces<IReadOnlyList<RoleSummary>>(StatusCodes.Status200OK)
            .WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        app.MapGet("/api/v1/members", async (IQueryDispatcher d, CancellationToken ct) =>
            Results.Ok(await d.SendAsync<GetMembersQuery, IReadOnlyList<MemberSummary>>(new GetMembersQuery(), ct)))
            .WithName("GetMembers").WithTags("Access Management")
            .Produces<IReadOnlyList<MemberSummary>>(StatusCodes.Status200OK)
            .WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        return app;
    }
}
```

In `Program.cs`, add usings and register: `app.MapPrincipalGrants(versionSet); app.MapMembers(versionSet);`

- [ ] **Step 7: Build + commit**

Run: `dotnet build developer-platform-reference.slnx --no-restore` → 0/0.

```bash
git add -A
git commit -m "feat(authz): grant-management commands, queries, and endpoints"
```

---

## Task 3: Member invitations

**Files:** Application: `Members/InviteMember/InviteMemberCommand.cs`, `Members/RevokeInvitation/RevokeInvitationCommand.cs`, `Members/GetInvitations/GetInvitationsQuery.cs`. Infrastructure handlers under `Infrastructure/Members/`. Api: add invitation routes to `MembersEndpoints.cs`. Modify `ServiceCollectionExtensions.cs`. Test: `InvitationTests.cs`.

**Interfaces produced:**
- `InviteMemberCommand(string Email, Guid RoleId, ScopeType ScopeType, Guid? ScopeTargetId) : ICommand<InviteMemberResult>` `[RequiresPermission(MembersManage)]` → `InviteMemberResult(Guid InvitationId, string Token)`.
- `RevokeInvitationCommand(Guid InvitationId) : ICommand` `[MembersManage]`.
- `GetInvitationsQuery : IQuery<IReadOnlyList<InvitationSummary>>` `[MembersManage]` → `InvitationSummary(Guid Id, string Email, Guid RoleId, string Status, DateTime ExpiresAt)`.

- [ ] **Step 1: Write the failing test**

Create `tests/DeveloperPlatform.Api.Tests/Authorization/InvitationTests.cs`:

```csharp
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Members.InviteMember;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Authorization;
using DeveloperPlatform.Infrastructure.Members;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class InvitationTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();
    private Ctx _ctx = null!;

    public async Task InitializeAsync()
    {
        _ctx = new Ctx { TenantId = _tenant, PrincipalId = _actor };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new ApplicationDbContext(options, _ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
    }
    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private InviteMemberCommandHandler Handler() =>
        new(_db, _ctx, new PrivilegeGuard(new AuthorizationService(_db), _db));

    [Fact]
    public async Task Invite_To_Viewer_Requires_Actor_To_Hold_Viewer_Permissions()
    {
        // Viewer = ProjectsRead, SecretsRead, AuditRead (seeded). Actor holds none → denied.
        await Assert.ThrowsAsync<DeveloperPlatform.Application.Authorization.ForbiddenException>(
            () => Handler().HandleAsync(new InviteMemberCommand("new@example.com",
                DeveloperPlatform.Infrastructure.Authorization.SystemRoles.ViewerId, ScopeType.Tenant, null)));
    }

    [Fact]
    public async Task Invite_Creates_Pending_Invitation_When_Actor_Is_Owner()
    {
        // Owner role assignment for the actor → holds everything.
        _db.RoleAssignments.Add(RoleAssignment.Create(
            _tenant, _actor, DeveloperPlatform.Infrastructure.Authorization.SystemRoles.OwnerId, Scope.Tenant));
        await _db.SaveChangesAsync();

        var result = await Handler().HandleAsync(new InviteMemberCommand("new@example.com",
            DeveloperPlatform.Infrastructure.Authorization.SystemRoles.ViewerId, ScopeType.Tenant, null));

        var inv = await _db.Invitations.AsNoTracking().SingleAsync(i => i.Id == result.InvitationId);
        Assert.Equal("new@example.com", inv.Email);
        Assert.Equal(InvitationStatus.Pending, inv.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
    }

    private sealed class Ctx : IExecutionContext
    {
        public Guid TenantId { get; set; }
        public Guid? PrincipalId { get; set; }
        public PrincipalType? PrincipalType => DeveloperPlatform.Domain.Authorization.PrincipalType.Member;
        public Guid? UserId => null;
        public Guid? ProjectId => null;
        public Guid? EnvironmentId => null;
        public string IpAddress => "127.0.0.1";
        public bool IsCrossTenantOperation { get; set; }
    }
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~InvitationTests"` → FAIL.

- [ ] **Step 3: Create the Application contracts**

`.../Members/InviteMember/InviteMemberCommand.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Members.InviteMember;

[RequiresPermission(Permission.MembersManage)]
public record InviteMemberCommand(string Email, Guid RoleId, ScopeType ScopeType, Guid? ScopeTargetId)
    : ICommand<InviteMemberResult>;

public record InviteMemberResult(Guid InvitationId, string Token);
```

`.../Members/RevokeInvitation/RevokeInvitationCommand.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Members.RevokeInvitation;

[RequiresPermission(Permission.MembersManage)]
public record RevokeInvitationCommand(Guid InvitationId) : ICommand;
```

`.../Members/GetInvitations/GetInvitationsQuery.cs`:

```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Members.GetInvitations;

[RequiresPermission(Permission.MembersManage)]
public record GetInvitationsQuery : IQuery<IReadOnlyList<InvitationSummary>>;

public record InvitationSummary(Guid Id, string Email, Guid RoleId, string Status, DateTime ExpiresAt);
```

- [ ] **Step 4: Create the handlers**

`src/DeveloperPlatform.Infrastructure/Members/InviteMemberCommandHandler.cs`:

```csharp
using System.Security.Cryptography;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Members.InviteMember;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class InviteMemberCommandHandler(
    ApplicationDbContext db, IExecutionContext executionContext, IPrivilegeGuard guard)
    : ICommandHandler<InviteMemberCommand, InviteMemberResult>
{
    public async Task<InviteMemberResult> HandleAsync(InviteMemberCommand command, CancellationToken ct = default)
    {
        var scope = Scope.Create(command.ScopeType, command.ScopeTargetId);
        var actor = executionContext.PrincipalId ?? throw new ForbiddenException("No acting principal.");
        // You can only invite someone to a role you could grant yourself.
        await guard.EnsureCanAssignRoleAsync(actor, command.RoleId, scope, ct);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var invitation = Invitation.Create(
            executionContext.TenantId, command.Email, command.RoleId, scope, token, DateTime.UtcNow.AddDays(7));
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        return new InviteMemberResult(invitation.Id, token);
    }
}
```

`src/DeveloperPlatform.Infrastructure/Members/RevokeInvitationCommandHandler.cs`:

```csharp
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Members.RevokeInvitation;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class RevokeInvitationCommandHandler(ApplicationDbContext db)
    : ICommandHandler<RevokeInvitationCommand, Unit>
{
    public async Task<Unit> HandleAsync(RevokeInvitationCommand command, CancellationToken ct = default)
    {
        var inv = await db.Invitations.FirstOrDefaultAsync(i => i.Id == command.InvitationId, ct)
            ?? throw new KeyNotFoundException($"Invitation {command.InvitationId} not found.");
        inv.Revoke();
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

`src/DeveloperPlatform.Infrastructure/Members/GetInvitationsQueryHandler.cs`:

```csharp
using DeveloperPlatform.Application.Members.GetInvitations;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class GetInvitationsQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetInvitationsQuery, IReadOnlyList<InvitationSummary>>
{
    public async Task<IReadOnlyList<InvitationSummary>> HandleAsync(GetInvitationsQuery query, CancellationToken ct = default)
        => await db.Invitations.AsNoTracking().OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvitationSummary(i.Id, i.Email, i.RoleId, i.Status.ToString(), i.ExpiresAt))
            .ToListAsync(ct);
}
```

- [ ] **Step 5: Register + endpoints**

Register the 3 handlers in `ServiceCollectionExtensions.cs`. In `MembersEndpoints.cs`, add invitation routes:

```csharp
        app.MapPost("/api/v1/invitations", async (
            [Microsoft.AspNetCore.Mvc.FromBody] InviteRequest req, ICommandDispatcher d, CancellationToken ct) =>
        {
            var r = await d.SendAsync<DeveloperPlatform.Application.Members.InviteMember.InviteMemberCommand,
                DeveloperPlatform.Application.Members.InviteMember.InviteMemberResult>(
                new(req.Email, req.RoleId, req.ScopeType, req.ScopeTargetId), ct);
            return Results.Created($"/api/v1/invitations/{r.InvitationId}", r);
        }).WithName("InviteMember").WithTags("Access Management")
          .Produces<DeveloperPlatform.Application.Members.InviteMember.InviteMemberResult>(StatusCodes.Status201Created)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        app.MapGet("/api/v1/invitations", async (IQueryDispatcher d, CancellationToken ct) =>
            Results.Ok(await d.SendAsync<DeveloperPlatform.Application.Members.GetInvitations.GetInvitationsQuery,
                IReadOnlyList<DeveloperPlatform.Application.Members.GetInvitations.InvitationSummary>>(new(), ct)))
            .WithName("GetInvitations").WithTags("Access Management")
            .WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        app.MapPost("/api/v1/invitations/{invitationId:guid}/revoke", async (
            Guid invitationId, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<DeveloperPlatform.Application.Members.RevokeInvitation.RevokeInvitationCommand,
                DeveloperPlatform.Application.Commands.Unit>(new(invitationId), ct);
            return Results.NoContent();
        }).WithName("RevokeInvitation").WithTags("Access Management")
          .WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();
```

Add the request record to `MembersEndpoints`: `public record InviteRequest(string Email, Guid RoleId, DeveloperPlatform.Domain.Authorization.ScopeType ScopeType, Guid? ScopeTargetId);`

- [ ] **Step 6: Run tests, build, commit**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~InvitationTests"` → PASS (2). Build 0/0.

```bash
git add -A
git commit -m "feat(authz): member invitations (invite/list/revoke) with escalation guard"
```

---

## Task 4: Invitation-gated onboarding + tenant-key provisioning

**Files:** Modify `src/DeveloperPlatform.Infrastructure/Authorization/PrincipalResolver.cs`, `ServiceCollectionExtensions.cs` (PrincipalResolver now needs `ITenantCryptoService`); update `tests/DeveloperPlatform.Api.Tests/Authorization/PrincipalResolverTests.cs`.

**Interfaces:** `PrincipalResolver` constructor gains `ITenantCryptoService cryptoService` (after `ApplicationDbContext db`). Behaviour: first member → Owner + `CreateKeyAsync`; matching pending invitation → its role + `Accept()`; otherwise → `null`.

- [ ] **Step 1: Update `PrincipalResolverTests`**

Read the file. Its `_sut` is constructed as `new PrincipalResolver(_db)`. Change to `new PrincipalResolver(_db, new DeveloperPlatform.Infrastructure.Crypto.TenantCryptoService(_db, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)))`. Then update/extend the tests:
- `First_Member_Becomes_Owner` (existing) — still passes; ALSO assert a `TenantEncryptionKey` now exists for the tenant: `Assert.True(await _db.TenantEncryptionKeys.AnyAsync())`.
- Replace `Second_Member_Gets_No_Role` with `Second_Member_Without_Invitation_Gets_No_Membership`:
```csharp
    [Fact]
    public async Task Second_Member_Without_Invitation_Gets_No_Membership()
    {
        await _sut.ResolveAsync(WithSubject("kc-first"), _tenant);       // first → Owner
        var second = await _sut.ResolveAsync(WithSubject("kc-second"), _tenant);
        Assert.Null(second);                                             // no invite → not a member
    }
```
- Add `Invited_User_Gets_Invited_Role_And_Invitation_Accepted`:
```csharp
    [Fact]
    public async Task Invited_User_Gets_Invited_Role_And_Invitation_Accepted()
    {
        await _sut.ResolveAsync(WithSubject("kc-first"), _tenant);       // establish tenant (Owner)
        var roleId = DeveloperPlatform.Infrastructure.Authorization.SystemRoles.ViewerId;
        _db.Invitations.Add(DeveloperPlatform.Domain.Authorization.Invitation.Create(
            _tenant, "invitee@example.com", roleId,
            DeveloperPlatform.Domain.Authorization.Scope.Tenant, "tok", DateTime.UtcNow.AddDays(1)));
        await _db.SaveChangesAsync();

        var claims = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("sub", "kc-invitee"),
            new System.Security.Claims.Claim("email", "invitee@example.com"),
        }));
        var resolved = await _sut.ResolveAsync(claims, _tenant);

        Assert.NotNull(resolved);
        Assert.True(await _db.RoleAssignments.AnyAsync(a => a.PrincipalId == resolved!.PrincipalId && a.RoleId == roleId));
        Assert.True(await _db.Invitations.AnyAsync(i =>
            i.Email == "invitee@example.com" && i.Status == DeveloperPlatform.Domain.Authorization.InvitationStatus.Accepted));
    }
```
(The existing `WithSubject` helper sets `email` to `u@example.com`; for the "first"/"second" cases that's fine.)

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~PrincipalResolverTests"` → FAIL (ctor arity + new behaviour).

- [ ] **Step 3: Rework `PrincipalResolver`**

Replace the constructor and the "no membership" branch. New file:

```csharp
using System.Security.Claims;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Authorization;

public sealed class PrincipalResolver(ApplicationDbContext db, ITenantCryptoService cryptoService)
    : IPrincipalResolver
{
    public async Task<ResolvedPrincipal?> ResolveAsync(
        ClaimsPrincipal user, Guid tenantId, CancellationToken ct = default)
    {
        var subject = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

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

        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == dbUser.Id, ct);
        if (membership is not null)
        {
            return new ResolvedPrincipal(membership.PrincipalId, PrincipalType.Member, dbUser.Id);
        }

        // First member of the tenant → Owner, and provision the tenant encryption key.
        if (!await db.Memberships.AnyAsync(ct))
        {
            var owner = Principal.CreateMember(tenantId, dbUser.DisplayName);
            db.Principals.Add(owner);
            db.Memberships.Add(Membership.Create(tenantId, owner.Id, dbUser.Id, MembershipStatus.Active));
            db.RoleAssignments.Add(RoleAssignment.Create(tenantId, owner.Id, SystemRoles.OwnerId, Scope.Tenant));
            await cryptoService.CreateKeyAsync(tenantId, ct);   // adds a TenantEncryptionKey to the context
            await db.SaveChangesAsync(ct);
            return new ResolvedPrincipal(owner.Id, PrincipalType.Member, dbUser.Id);
        }

        // Otherwise require a matching pending invitation (invitation-gated onboarding).
        var invitation = await db.Invitations.FirstOrDefaultAsync(
            i => i.Email == dbUser.Email && i.Status == InvitationStatus.Pending && i.ExpiresAt > DateTime.UtcNow, ct);
        if (invitation is null)
        {
            return null;   // not a member, no invitation → 403 downstream
        }

        var principal = Principal.CreateMember(tenantId, dbUser.DisplayName);
        db.Principals.Add(principal);
        db.Memberships.Add(Membership.Create(tenantId, principal.Id, dbUser.Id, MembershipStatus.Active));
        db.RoleAssignments.Add(RoleAssignment.Create(tenantId, principal.Id, invitation.RoleId, invitation.Scope));
        invitation.Accept();
        await db.SaveChangesAsync(ct);
        return new ResolvedPrincipal(principal.Id, PrincipalType.Member, dbUser.Id);
    }
}
```

- [ ] **Step 4: Fix the DI registration**

`PrincipalResolver` now takes `ITenantCryptoService`. The existing `services.AddScoped<IPrincipalResolver, PrincipalResolver>();` resolves it from DI (`ITenantCryptoService` is already registered) — no change needed unless it was constructed manually anywhere (it is not). Build to confirm.

- [ ] **Step 5: Run tests, build, commit**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~PrincipalResolverTests"` → PASS. Build 0/0.

```bash
git add -A
git commit -m "feat(authz): invitation-gated onboarding + tenant-key provisioning at bootstrap"
```

---

## Task 5: Fixes — string enums, membership uniqueness, service-account escalation guard

**Files:** Modify `Program.cs` (JsonStringEnumConverter), `MembershipConfiguration.cs` (unique index), `CreateServiceAccountCommandHandler.cs` (escalation guard). Generate a migration. Test: extend an existing test or add a small one.

- [ ] **Step 1: Register `JsonStringEnumConverter` globally**

In `src/DeveloperPlatform.Api/Program.cs`, after `builder.Services.AddProblemDetails();` add:

```csharp
    builder.Services.ConfigureHttpJsonOptions(options =>
        options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
```

This makes Minimal-API request/response bodies accept and emit enum NAMES (`"ProjectsRead"`, `"Tenant"`), and a bad name yields 400 (bind failure) rather than 500.

- [ ] **Step 2: Add the escalation guard to service-account creation**

In `src/DeveloperPlatform.Infrastructure/ApiKeys/CreateServiceAccountCommandHandler.cs`, inject `IPrivilegeGuard guard` (add as the last ctor param) and, before creating the grants, verify the actor holds each:

```csharp
        var actor = executionContext.PrincipalId
            ?? throw new DeveloperPlatform.Application.Authorization.ForbiddenException("No acting principal.");
        foreach (var g in command.Grants)
        {
            await guard.EnsureCanGrantAsync(actor, g.Permission, Scope.Create(g.ScopeType, g.ScopeTargetId), ct);
        }
```

(Add `using DeveloperPlatform.Application.Authorization;`. The guard is already registered in DI from Task 2.)

- [ ] **Step 3: Unique index on `Membership(TenantId, UserId)`**

In `src/DeveloperPlatform.Infrastructure/Persistence/Configurations/MembershipConfiguration.cs`, add:

```csharp
        builder.HasIndex(m => new { m.TenantId, m.UserId }).IsUnique();
```

- [ ] **Step 4: Generate the migration**

`docker compose up -d db` (healthy). With `127.0.0.1`:
Run: `dotnet ef migrations add MembershipUniqueIndex --project src/DeveloperPlatform.Infrastructure --startup-project src/DeveloperPlatform.Infrastructure`
Verify it creates a unique index on `Memberships (TenantId, UserId)`. Strip BOM if needed.

- [ ] **Step 5: Build + run the authorization suite**

Run: `dotnet build developer-platform-reference.slnx --no-restore` → 0/0.
Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter "FullyQualifiedName~Api.Tests.Authorization"` → PASS.
Run: `dotnet test tests/DeveloperPlatform.ArchitectureTests` → PASS (10).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "fix(authz): string-enum binding, membership uniqueness, SA-grant escalation guard"
```

---

## Self-Review

**1. Spec coverage:** escalation guard (Task 1) ✅; grant management + roles/members reads (Task 2) ✅; invitations invite/list/revoke (Task 3) ✅; invitation-gated onboarding + key provisioning (Task 4) ✅; string-enum fix + membership uniqueness + SA-grant guard (Task 5) ✅. Deferred: composite `(PrincipalId, TenantId)` FK; custom roles.

**2. Placeholder scan:** No `TBD`/`TODO`. Every code step is complete; migrations are tool-generated with verification.

**3. Type consistency:** `IPrivilegeGuard.{EnsureCanGrantAsync,EnsureCanAssignRoleAsync}`; command records (`AssignRoleCommand`/`GrantPermissionCommand`/`InviteMemberCommand` + results); handlers take `(ApplicationDbContext, IExecutionContext, IPrivilegeGuard)`; `PrincipalResolver(ApplicationDbContext, ITenantCryptoService)`; endpoints dispatch the matching command/result types. All consistent across definitions, registrations, and tests.

**4. Risk notes:** Task 4 is the behaviour change (uninvited users lose access) — the reworked resolver returns null for them, so guarded ops 403. The `JsonStringEnumConverter` also changes response shape (enums become strings) — the `/permissions` endpoint already emits strings via `.ToString()`, so this is consistent, but any client parsing enum ints must adapt (none in this repo). Membership uniqueness closes the concurrent-first-login double-membership window from Slice 3's review.
