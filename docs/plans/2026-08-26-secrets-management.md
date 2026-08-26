# Secrets Management (Phase 5) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Store, read, and rotate per-environment encrypted secrets, gated by `secrets:*` permissions and recorded through the existing audit outbox.

**Architecture:** Pure vertical-slice CQRS mirroring the existing codebase. Every operation is a command/query dispatched through `CommandDispatcher`/`QueryDispatcher`, inheriting scope-aware authorization, auditing, and the transaction boundary. Secrets are addressed by nested REST routes carrying project + environment ids; commands/queries implement `IResourceScoped` so the permission is checked at `Scope.Environment`. Crypto uses the existing `ITenantCryptoService` (per-tenant AES-256-GCM with key versioning).

**Tech Stack:** .NET 10, EF Core 10 + Pomelo MySQL, minimal APIs with Asp.Versioning, Blazor Server + MudBlazor v9, xUnit + EF in-memory for tests.

## Global Constraints

- All mutations go through `CommandDispatcher` (audited, transactional); reads through `QueryDispatcher` — **except `RevealSecretCommand`, which is a command** so the value access is audited.
- Plaintext secret values must never reach the audit log: the value property on `SetSecretCommand` is marked `[SensitiveData]` so `SensitiveDataScrubber` redacts it.
- Secret op scope is `Scope.Environment(EnvironmentId)` from the route via `IResourceScoped` — never from JWT claims. `Scope.Encompasses` already lets a `Project`/`Tenant` grant satisfy an `Environment` check.
- Handlers add/remove entities to `ApplicationDbContext`; they do **not** call `SaveChanges` (the `CommandDispatcher` does) — the one exception is `RotateTenantKeyCommandHandler`, which flushes the new key mid-handler.
- Key rotation retains old keys (never calls `ShredKeyAsync`) so historical audit payloads stay decryptable.
- `secrets:read` / `secrets:write` already exist in `Permission`; do **not** add new permissions.
- Repo conventions: no AI co-author trailers in commits; hooks must pass (never `--no-verify`); `.cs` files are CRLF (`.gitattributes`).
- Migrations: `dotnet ef migrations add <Name> --project src/DeveloperPlatform.Infrastructure --startup-project src/DeveloperPlatform.Api`.

## Conventions reference (read once)

- **Command:** `[RequiresPermission(Permission.X)] public record FooCommand(...) : ICommand<TResult>, IResourceScoped { public Scope ResourceScope => Scope.Environment(EnvironmentId); }`. Void result → `ICommand<Unit>`.
- **Query:** `[RequiresPermission(Permission.X)] public record FooQuery(...) : IQuery<TResult>, IResourceScoped { public Scope ResourceScope => ...; }`.
- **Handler:** `public sealed class FooCommandHandler(ApplicationDbContext db, IExecutionContext ctx) : ICommandHandler<FooCommand, TResult> { public async Task<TResult> HandleAsync(FooCommand command, CancellationToken ct = default) {...} }`.
- **Repository:** interface + `sealed class FooRepository(ApplicationDbContext db)` wrapping `db.Set`.
- **DI:** register every handler + repo in `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs`.
- **Endpoints:** `public static class FooEndpoints { public static IEndpointRouteBuilder MapFoo(this IEndpointRouteBuilder app, ApiVersionSet versionSet) {...} }`, registered in `src/DeveloperPlatform.Api/Program.cs`.
- **Handler unit test harness:**
  ```csharp
  var ctx = new TestExecutionContext { TenantId = _tenant };
  var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
  _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
  await _db.Database.EnsureCreatedAsync();
  var crypto = new TenantCryptoService(_db, Key);        // Key = RandomNumberGenerator.GetBytes(32)
  await crypto.CreateKeyAsync(_tenant); await _db.SaveChangesAsync();
  ```
  `TestExecutionContext` is a private nested `IExecutionContext` (copy from `IssueApiKeyTests`).
- **Dispatcher test harness** (for authz/audit): copy `EnforcementTests.Build()` — a `CommandDispatcher` built from a `ServiceCollection` (handler registered), `AuthorizationService(_db)`, `TenantCryptoService(_db, Key)`, `AuditOutboxRepository(_db)`, `SensitiveDataScrubber()`, `TenancyMode.SharedTables`. Seed permission with `PermissionGrant.Create(tenant, principal, Permission.X, Scope.Y)`.

## File Structure

**Slice A — Environments**
- Modify `src/DeveloperPlatform.Domain/Projects/ProjectEnvironment.cs` — add `Rename`.
- Create `src/DeveloperPlatform.Infrastructure/Persistence/Configurations/ProjectEnvironmentConfiguration.cs` — unique `(ProjectId, Name)`.
- Create `src/DeveloperPlatform.Infrastructure/Projects/IProjectEnvironmentRepository.cs` + `ProjectEnvironmentRepository.cs`.
- Create `src/DeveloperPlatform.Application/Environments/{CreateEnvironment,RenameEnvironment,DeleteEnvironment,GetEnvironments}/*.cs`.
- Create `src/DeveloperPlatform.Infrastructure/Environments/*Handler.cs`.
- Create `src/DeveloperPlatform.Api/Endpoints/Environments/EnvironmentsEndpoints.cs`.
- Test `tests/DeveloperPlatform.Api.Tests/Secrets/EnvironmentTests.cs`.

**Slice B — Secrets**
- Modify `src/DeveloperPlatform.Domain/Secrets/Secret.cs` — add `UpdatedAt`.
- Create `src/DeveloperPlatform.Infrastructure/Secrets/ISecretRepository.cs` + `SecretRepository.cs`.
- Create `src/DeveloperPlatform.Application/Secrets/{SetSecret,ListSecrets,RevealSecret,DeleteSecret}/*.cs`.
- Create `src/DeveloperPlatform.Infrastructure/Secrets/*Handler.cs`.
- Create `src/DeveloperPlatform.Api/Endpoints/Secrets/SecretsEndpoints.cs`.
- Test `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`, `SecretAuthorizationTests.cs`.

**Slice C — Rotation**
- Create `src/DeveloperPlatform.Application/Secrets/RotateTenantKey/RotateTenantKeyCommand.cs`.
- Create `src/DeveloperPlatform.Infrastructure/Secrets/RotateTenantKeyCommandHandler.cs`.
- Add rotate route to `SecretsEndpoints.cs`.
- Test `tests/DeveloperPlatform.Api.Tests/Secrets/RotationTests.cs`.

**Slice D — Web UI**
- Modify `src/DeveloperPlatform.Web/Http/DeveloperPlatformApiClient.cs` + `Http/Models/AccessDtos.cs` (or new `SecretDtos.cs`).
- Create `src/DeveloperPlatform.Web/Components/Pages/ProjectDetail.razor` + `SecretDialog.razor` + `EnvironmentDialog.razor`.
- Modify `src/DeveloperPlatform.Web/Components/Pages/Projects.razor` (row → link to detail) and `NavMenu.razor` if needed.
- Add a rotate-key control (e.g., a `Settings.razor` page or a section on ProjectDetail's tenant area).
- Test `tests/DeveloperPlatform.Web.Tests/Http/DeveloperPlatformApiClientTests.cs` (client methods).

---

## Slice A — Environment management

### Task A1: ProjectEnvironment domain + repository + config

**Files:**
- Modify: `src/DeveloperPlatform.Domain/Projects/ProjectEnvironment.cs`
- Create: `src/DeveloperPlatform.Infrastructure/Persistence/Configurations/ProjectEnvironmentConfiguration.cs`
- Create: `src/DeveloperPlatform.Infrastructure/Projects/IProjectEnvironmentRepository.cs`, `ProjectEnvironmentRepository.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Secrets/EnvironmentTests.cs`

**Interfaces:**
- Produces: `ProjectEnvironment.Rename(string name)`; `IProjectEnvironmentRepository { Task AddAsync(ProjectEnvironment, CancellationToken); Task<ProjectEnvironment?> GetAsync(Guid projectId, Guid environmentId, CancellationToken); Task<IReadOnlyList<ProjectEnvironment>> ListAsync(Guid projectId, CancellationToken); void Delete(ProjectEnvironment); }`

- [ ] **Step 1: Write the failing test**

Create `tests/DeveloperPlatform.Api.Tests/Secrets/EnvironmentTests.cs`:
```csharp
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Projects;
using DeveloperPlatform.Infrastructure.Persistence;
using DeveloperPlatform.Infrastructure.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DeveloperPlatform.Api.Tests.Secrets;

public class EnvironmentTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _project = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var ctx = new TestExecutionContext { TenantId = _tenant };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Repository_Adds_And_Lists_By_Project()
    {
        var repo = new ProjectEnvironmentRepository(_db);
        await repo.AddAsync(ProjectEnvironment.Create(_tenant, _project, "Production", EnvironmentType.Production));
        await _db.SaveChangesAsync();

        var list = await repo.ListAsync(_project);
        Assert.Single(list);
        Assert.Equal("Production", list[0].Name);
    }

    [Fact]
    public void Rename_Rejects_Blank()
    {
        var env = ProjectEnvironment.Create(_tenant, _project, "Dev", EnvironmentType.Development);
        Assert.Throws<ArgumentException>(() => env.Rename(" "));
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

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter EnvironmentTests`
Expected: FAIL — `ProjectEnvironmentRepository` and `Rename` do not exist.

- [ ] **Step 3: Add `Rename` to the domain**

In `src/DeveloperPlatform.Domain/Projects/ProjectEnvironment.cs`, add after `Create`:
```csharp
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
```

- [ ] **Step 4: Add the EF configuration**

Create `src/DeveloperPlatform.Infrastructure/Persistence/Configurations/ProjectEnvironmentConfiguration.cs`:
```csharp
using DeveloperPlatform.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class ProjectEnvironmentConfiguration : IEntityTypeConfiguration<ProjectEnvironment>
{
    public void Configure(EntityTypeBuilder<ProjectEnvironment> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(e => new { e.ProjectId, e.Name }).IsUnique();
    }
}
```
(`ApplicationDbContext.OnModelCreating` calls `modelBuilder.ApplyConfigurationsFromAssembly(...)`, so this configuration is auto-discovered — no further wiring needed.)

- [ ] **Step 5: Add the repository**

Create `src/DeveloperPlatform.Infrastructure/Projects/IProjectEnvironmentRepository.cs`:
```csharp
using DeveloperPlatform.Domain.Projects;

namespace DeveloperPlatform.Infrastructure.Projects;

public interface IProjectEnvironmentRepository
{
    Task AddAsync(ProjectEnvironment environment, CancellationToken ct = default);
    Task<ProjectEnvironment?> GetAsync(Guid projectId, Guid environmentId, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectEnvironment>> ListAsync(Guid projectId, CancellationToken ct = default);
    void Delete(ProjectEnvironment environment);
}
```
Create `src/DeveloperPlatform.Infrastructure/Projects/ProjectEnvironmentRepository.cs`:
```csharp
using DeveloperPlatform.Domain.Projects;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Projects;

public sealed class ProjectEnvironmentRepository(ApplicationDbContext db) : IProjectEnvironmentRepository
{
    public async Task AddAsync(ProjectEnvironment environment, CancellationToken ct = default)
        => await db.ProjectEnvironments.AddAsync(environment, ct);

    public async Task<ProjectEnvironment?> GetAsync(Guid projectId, Guid environmentId, CancellationToken ct = default)
        => await db.ProjectEnvironments
            .FirstOrDefaultAsync(e => e.ProjectId == projectId && e.Id == environmentId, ct);

    public async Task<IReadOnlyList<ProjectEnvironment>> ListAsync(Guid projectId, CancellationToken ct = default)
        => await db.ProjectEnvironments.AsNoTracking()
            .Where(e => e.ProjectId == projectId)
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

    public void Delete(ProjectEnvironment environment) => db.ProjectEnvironments.Remove(environment);
}
```
(The `DbSet` is `ApplicationDbContext.ProjectEnvironments`.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter EnvironmentTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add src/DeveloperPlatform.Domain/Projects/ProjectEnvironment.cs \
        src/DeveloperPlatform.Infrastructure/Persistence/Configurations/ProjectEnvironmentConfiguration.cs \
        src/DeveloperPlatform.Infrastructure/Projects/IProjectEnvironmentRepository.cs \
        src/DeveloperPlatform.Infrastructure/Projects/ProjectEnvironmentRepository.cs \
        tests/DeveloperPlatform.Api.Tests/Secrets/EnvironmentTests.cs
git commit -m "feat(secrets): ProjectEnvironment repository + rename, unique (project,name)"
```

### Task A2: Environment commands, queries, handlers, DI, endpoints

**Files:**
- Create: `src/DeveloperPlatform.Application/Environments/CreateEnvironment/CreateEnvironmentCommand.cs`, `RenameEnvironment/RenameEnvironmentCommand.cs`, `DeleteEnvironment/DeleteEnvironmentCommand.cs`, `GetEnvironments/GetEnvironmentsQuery.cs`
- Create: `src/DeveloperPlatform.Infrastructure/Environments/CreateEnvironmentCommandHandler.cs`, `RenameEnvironmentCommandHandler.cs`, `DeleteEnvironmentCommandHandler.cs`, `GetEnvironmentsQueryHandler.cs`
- Modify: `src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs`
- Create: `src/DeveloperPlatform.Api/Endpoints/Environments/EnvironmentsEndpoints.cs`
- Modify: `src/DeveloperPlatform.Api/Program.cs`
- Test: `tests/DeveloperPlatform.Api.Tests/Secrets/EnvironmentTests.cs` (extend)

**Interfaces:**
- Consumes: `IProjectEnvironmentRepository` (Task A1); `Secret` DbSet for cascade.
- Produces: `CreateEnvironmentCommand(Guid ProjectId, string Name, EnvironmentType Type) : ICommand<CreateEnvironmentResult>`, `CreateEnvironmentResult(Guid EnvironmentId)`; `RenameEnvironmentCommand(Guid ProjectId, Guid EnvironmentId, string Name) : ICommand<Unit>`; `DeleteEnvironmentCommand(Guid ProjectId, Guid EnvironmentId) : ICommand<Unit>`; `GetEnvironmentsQuery(Guid ProjectId) : IQuery<IReadOnlyList<EnvironmentSummary>>`, `EnvironmentSummary(Guid Id, string Name, EnvironmentType Type, DateTime CreatedAt)`.

- [ ] **Step 1: Write the failing test** (append to `EnvironmentTests.cs`)
```csharp
    [Fact]
    public async Task CreateHandler_Persists_Environment()
    {
        var repo = new ProjectEnvironmentRepository(_db);
        var handler = new DeveloperPlatform.Infrastructure.Environments.CreateEnvironmentCommandHandler(
            repo, new TestExecutionContext { TenantId = _tenant });
        var result = await handler.HandleAsync(
            new DeveloperPlatform.Application.Environments.CreateEnvironment.CreateEnvironmentCommand(
                _project, "Staging", EnvironmentType.Staging));
        await _db.SaveChangesAsync();

        Assert.NotEqual(Guid.Empty, result.EnvironmentId);
        Assert.Single(await repo.ListAsync(_project));
    }

    [Fact]
    public async Task DeleteHandler_Cascades_To_Secrets()
    {
        var env = ProjectEnvironment.Create(_tenant, _project, "Dev", EnvironmentType.Development);
        _db.ProjectEnvironments.Add(env);
        _db.Secrets.Add(DeveloperPlatform.Domain.Secrets.Secret.Create(
            _tenant, _project, env.Id, "API_KEY", new byte[] { 1, 2, 3 }, Guid.NewGuid()));
        await _db.SaveChangesAsync();

        var handler = new DeveloperPlatform.Infrastructure.Environments.DeleteEnvironmentCommandHandler(
            new ProjectEnvironmentRepository(_db), _db);
        await handler.HandleAsync(
            new DeveloperPlatform.Application.Environments.DeleteEnvironment.DeleteEnvironmentCommand(_project, env.Id));
        await _db.SaveChangesAsync();

        Assert.Empty(await _db.ProjectEnvironments.ToListAsync());
        Assert.Empty(await _db.Secrets.ToListAsync());
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter EnvironmentTests`
Expected: FAIL — command/handler types do not exist.

- [ ] **Step 3: Create the commands/query**

`CreateEnvironment/CreateEnvironmentCommand.cs`:
```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Projects;

namespace DeveloperPlatform.Application.Environments.CreateEnvironment;

[RequiresPermission(Permission.ProjectsWrite)]
public record CreateEnvironmentCommand(Guid ProjectId, string Name, EnvironmentType Type)
    : ICommand<CreateEnvironmentResult>, IResourceScoped
{
    public Scope ResourceScope => Scope.Project(ProjectId);
}

public record CreateEnvironmentResult(Guid EnvironmentId);
```
`RenameEnvironment/RenameEnvironmentCommand.cs`:
```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Environments.RenameEnvironment;

[RequiresPermission(Permission.ProjectsWrite)]
public record RenameEnvironmentCommand(Guid ProjectId, Guid EnvironmentId, string Name)
    : ICommand<Unit>, IResourceScoped
{
    public Scope ResourceScope => Scope.Project(ProjectId);
}
```
`DeleteEnvironment/DeleteEnvironmentCommand.cs`:
```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Environments.DeleteEnvironment;

[RequiresPermission(Permission.ProjectsWrite)]
public record DeleteEnvironmentCommand(Guid ProjectId, Guid EnvironmentId)
    : ICommand<Unit>, IResourceScoped
{
    public Scope ResourceScope => Scope.Project(ProjectId);
}
```
`GetEnvironments/GetEnvironmentsQuery.cs`:
```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Projects;

namespace DeveloperPlatform.Application.Environments.GetEnvironments;

[RequiresPermission(Permission.ProjectsRead)]
public record GetEnvironmentsQuery(Guid ProjectId) : IQuery<IReadOnlyList<EnvironmentSummary>>, IResourceScoped
{
    public Scope ResourceScope => Scope.Project(ProjectId);
}

public record EnvironmentSummary(Guid Id, string Name, EnvironmentType Type, DateTime CreatedAt);
```

- [ ] **Step 4: Create the handlers**

`Environments/CreateEnvironmentCommandHandler.cs`:
```csharp
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Environments.CreateEnvironment;
using DeveloperPlatform.Domain.Projects;
using DeveloperPlatform.Infrastructure.Projects;

namespace DeveloperPlatform.Infrastructure.Environments;

public sealed class CreateEnvironmentCommandHandler(
    IProjectEnvironmentRepository repository, IExecutionContext ctx)
    : ICommandHandler<CreateEnvironmentCommand, CreateEnvironmentResult>
{
    public async Task<CreateEnvironmentResult> HandleAsync(CreateEnvironmentCommand command, CancellationToken ct = default)
    {
        var env = ProjectEnvironment.Create(ctx.TenantId, command.ProjectId, command.Name, command.Type);
        await repository.AddAsync(env, ct);
        return new CreateEnvironmentResult(env.Id);
    }
}
```
`Environments/RenameEnvironmentCommandHandler.cs`:
```csharp
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Environments.RenameEnvironment;
using DeveloperPlatform.Infrastructure.Projects;

namespace DeveloperPlatform.Infrastructure.Environments;

public sealed class RenameEnvironmentCommandHandler(IProjectEnvironmentRepository repository)
    : ICommandHandler<RenameEnvironmentCommand, Unit>
{
    public async Task<Unit> HandleAsync(RenameEnvironmentCommand command, CancellationToken ct = default)
    {
        var env = await repository.GetAsync(command.ProjectId, command.EnvironmentId, ct)
            ?? throw new KeyNotFoundException($"Environment {command.EnvironmentId} not found.");
        env.Rename(command.Name);
        return Unit.Value;
    }
}
```
`Environments/DeleteEnvironmentCommandHandler.cs` (cascade to secrets):
```csharp
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Environments.DeleteEnvironment;
using DeveloperPlatform.Infrastructure.Persistence;
using DeveloperPlatform.Infrastructure.Projects;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Environments;

public sealed class DeleteEnvironmentCommandHandler(
    IProjectEnvironmentRepository repository, ApplicationDbContext db)
    : ICommandHandler<DeleteEnvironmentCommand, Unit>
{
    public async Task<Unit> HandleAsync(DeleteEnvironmentCommand command, CancellationToken ct = default)
    {
        var env = await repository.GetAsync(command.ProjectId, command.EnvironmentId, ct)
            ?? throw new KeyNotFoundException($"Environment {command.EnvironmentId} not found.");

        var secrets = await db.Secrets.Where(s => s.EnvironmentId == env.Id).ToListAsync(ct);
        db.Secrets.RemoveRange(secrets);
        repository.Delete(env);
        return Unit.Value;
    }
}
```
`Environments/GetEnvironmentsQueryHandler.cs`:
```csharp
using DeveloperPlatform.Application.Environments.GetEnvironments;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Environments;

public sealed class GetEnvironmentsQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetEnvironmentsQuery, IReadOnlyList<EnvironmentSummary>>
{
    public async Task<IReadOnlyList<EnvironmentSummary>> HandleAsync(GetEnvironmentsQuery query, CancellationToken ct = default)
        => await db.ProjectEnvironments.AsNoTracking()
            .Where(e => e.ProjectId == query.ProjectId)
            .OrderBy(e => e.Name)
            .Select(e => new EnvironmentSummary(e.Id, e.Name, e.Type, e.CreatedAt))
            .ToListAsync(ct);
}
```

- [ ] **Step 5: Register DI** — in `ServiceCollectionExtensions.cs`, add a `// Environments (Slice A)` block:
```csharp
services.AddScoped<IProjectEnvironmentRepository, ProjectEnvironmentRepository>();
services.AddScoped<ICommandHandler<CreateEnvironmentCommand, CreateEnvironmentResult>, CreateEnvironmentCommandHandler>();
services.AddScoped<ICommandHandler<RenameEnvironmentCommand, Unit>, RenameEnvironmentCommandHandler>();
services.AddScoped<ICommandHandler<DeleteEnvironmentCommand, Unit>, DeleteEnvironmentCommandHandler>();
services.AddScoped<IQueryHandler<GetEnvironmentsQuery, IReadOnlyList<EnvironmentSummary>>, GetEnvironmentsQueryHandler>();
```
Add the matching `using DeveloperPlatform.Application.Environments.*;` and `using DeveloperPlatform.Infrastructure.Environments;` imports.

- [ ] **Step 6: Create the endpoints**

`src/DeveloperPlatform.Api/Endpoints/Environments/EnvironmentsEndpoints.cs`:
```csharp
using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Environments.CreateEnvironment;
using DeveloperPlatform.Application.Environments.DeleteEnvironment;
using DeveloperPlatform.Application.Environments.GetEnvironments;
using DeveloperPlatform.Application.Environments.RenameEnvironment;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Projects;
using Microsoft.AspNetCore.Mvc;

namespace DeveloperPlatform.Api.Endpoints.Environments;

public static class EnvironmentsEndpoints
{
    public static IEndpointRouteBuilder MapEnvironments(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v1/projects/{projectId:guid}/environments")
            .WithTags("Environments").WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IQueryDispatcher d, CancellationToken ct) =>
        {
            var results = await d.SendAsync<GetEnvironmentsQuery, IReadOnlyList<EnvironmentSummary>>(
                new GetEnvironmentsQuery(projectId), ct);
            return Results.Ok(results.Select(e => new EnvironmentResponse(e.Id, e.Name, e.Type.ToString(), e.CreatedAt)));
        }).WithName("GetEnvironments").Produces<IEnumerable<EnvironmentResponse>>();

        group.MapPost("/", async (Guid projectId, [FromBody] CreateEnvironmentRequest req, ICommandDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync<CreateEnvironmentCommand, CreateEnvironmentResult>(
                new CreateEnvironmentCommand(projectId, req.Name, Enum.Parse<EnvironmentType>(req.Type)), ct);
            return Results.Created($"/api/v1/projects/{projectId}/environments/{result.EnvironmentId}",
                new EnvironmentCreatedResponse(result.EnvironmentId));
        }).WithName("CreateEnvironment").Produces<EnvironmentCreatedResponse>(StatusCodes.Status201Created);

        group.MapPut("/{environmentId:guid}", async (Guid projectId, Guid environmentId, [FromBody] RenameEnvironmentRequest req, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<RenameEnvironmentCommand, Unit>(new RenameEnvironmentCommand(projectId, environmentId, req.Name), ct);
            return Results.NoContent();
        }).WithName("RenameEnvironment").Produces(StatusCodes.Status204NoContent);

        group.MapDelete("/{environmentId:guid}", async (Guid projectId, Guid environmentId, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<DeleteEnvironmentCommand, Unit>(new DeleteEnvironmentCommand(projectId, environmentId), ct);
            return Results.NoContent();
        }).WithName("DeleteEnvironment").Produces(StatusCodes.Status204NoContent);

        return app;
    }

    public record CreateEnvironmentRequest(string Name, string Type);
    public record RenameEnvironmentRequest(string Name);
    public record EnvironmentResponse(Guid Id, string Name, string Type, DateTime CreatedAt);
    public record EnvironmentCreatedResponse(Guid EnvironmentId);
}
```
Register in `Program.cs` after `app.MapProjects(versionSet);`:
```csharp
app.MapEnvironments(versionSet);
```
Add `using DeveloperPlatform.Api.Endpoints.Environments;` at the top of `Program.cs`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperPlatform.Api.Tests --filter EnvironmentTests`
Expected: PASS (4 tests). Then `dotnet build developer-platform-reference.slnx` — Expected: 0 errors.

- [ ] **Step 8: Commit**
```bash
git add src/DeveloperPlatform.Application/Environments src/DeveloperPlatform.Infrastructure/Environments \
        src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs \
        src/DeveloperPlatform.Api/Endpoints/Environments src/DeveloperPlatform.Api/Program.cs \
        tests/DeveloperPlatform.Api.Tests/Secrets/EnvironmentTests.cs
git commit -m "feat(secrets): environment CRUD (create/rename/delete cascade/list)"
```

---

## Slice B — Secrets CRUD

### Task B1: Secret domain UpdatedAt + repository + migration

**Files:**
- Modify: `src/DeveloperPlatform.Domain/Secrets/Secret.cs`
- Create: `src/DeveloperPlatform.Infrastructure/Secrets/ISecretRepository.cs`, `SecretRepository.cs`
- Migration: new EF migration
- Test: `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs`

**Interfaces:**
- Produces: `Secret.UpdatedAt` (DateTime, set on `Create` + `UpdateValue`); `ISecretRepository { Task<Secret?> GetAsync(Guid environmentId, string name, CancellationToken); Task<IReadOnlyList<Secret>> ListAsync(Guid environmentId, CancellationToken); Task AddAsync(Secret, CancellationToken); void Delete(Secret); }`

- [ ] **Step 1: Write the failing test**

Create `tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs` with the standard harness (copy Init/Dispose/`TestExecutionContext` from `EnvironmentTests`, adding `_env = Guid.NewGuid()` and a 32-byte `Key`), plus:
```csharp
    [Fact]
    public void UpdateValue_Sets_UpdatedAt_Later()
    {
        var s = Secret.Create(_tenant, _project, _env, "DB_URL", new byte[] { 1 }, Guid.NewGuid());
        var created = s.UpdatedAt;
        s.UpdateValue(new byte[] { 2 }, Guid.NewGuid());
        Assert.True(s.UpdatedAt >= created);
        Assert.Equal(2, s.EncryptedValue[0]);
    }

    [Fact]
    public async Task Repository_Get_By_Environment_And_Name()
    {
        var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
        await repo.AddAsync(Secret.Create(_tenant, _project, _env, "TOKEN", new byte[] { 9 }, Guid.NewGuid()));
        await _db.SaveChangesAsync();
        Assert.NotNull(await repo.GetAsync(_env, "TOKEN"));
        Assert.Null(await repo.GetAsync(_env, "MISSING"));
    }
```

- [ ] **Step 2: Run test to verify it fails** — `dotnet test ... --filter SecretTests` → FAIL (`UpdatedAt`, `SecretRepository` missing).

- [ ] **Step 3: Add `UpdatedAt` to `Secret.cs`** — add the property and set it:
```csharp
    public DateTime UpdatedAt { get; private set; }
```
In `Create`, set `UpdatedAt = DateTime.UtcNow` on the constructed object (add `UpdatedAt = DateTime.UtcNow,` to the initializer). In `UpdateValue`, add `UpdatedAt = DateTime.UtcNow;`.

- [ ] **Step 4: Add the repository**

`ISecretRepository.cs`:
```csharp
using DeveloperPlatform.Domain.Secrets;

namespace DeveloperPlatform.Infrastructure.Secrets;

public interface ISecretRepository
{
    Task<Secret?> GetAsync(Guid environmentId, string name, CancellationToken ct = default);
    Task<IReadOnlyList<Secret>> ListAsync(Guid environmentId, CancellationToken ct = default);
    Task AddAsync(Secret secret, CancellationToken ct = default);
    void Delete(Secret secret);
}
```
`SecretRepository.cs`:
```csharp
using DeveloperPlatform.Domain.Secrets;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class SecretRepository(ApplicationDbContext db) : ISecretRepository
{
    public async Task<Secret?> GetAsync(Guid environmentId, string name, CancellationToken ct = default)
        => await db.Secrets.FirstOrDefaultAsync(s => s.EnvironmentId == environmentId && s.Name == name, ct);

    public async Task<IReadOnlyList<Secret>> ListAsync(Guid environmentId, CancellationToken ct = default)
        => await db.Secrets.AsNoTracking()
            .Where(s => s.EnvironmentId == environmentId).OrderBy(s => s.Name).ToListAsync(ct);

    public async Task AddAsync(Secret secret, CancellationToken ct = default) => await db.Secrets.AddAsync(secret, ct);
    public void Delete(Secret secret) => db.Secrets.Remove(secret);
}
```

- [ ] **Step 5: Add the migration**

Run: `dotnet ef migrations add SecretUpdatedAt --project src/DeveloperPlatform.Infrastructure --startup-project src/DeveloperPlatform.Api`
Expected: a migration adding the `UpdatedAt` column to `Secrets`. Inspect it to confirm only that column (and any `ProjectEnvironment` index from Task A1 if not already migrated) is added.

- [ ] **Step 6: Run tests / build** — `dotnet test ... --filter SecretTests` → PASS; `dotnet build` → 0 errors.

- [ ] **Step 7: Commit**
```bash
git add src/DeveloperPlatform.Domain/Secrets/Secret.cs \
        src/DeveloperPlatform.Infrastructure/Secrets/ISecretRepository.cs \
        src/DeveloperPlatform.Infrastructure/Secrets/SecretRepository.cs \
        src/DeveloperPlatform.Infrastructure/Migrations \
        tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs
git commit -m "feat(secrets): Secret.UpdatedAt + repository + migration"
```

### Task B2: SetSecret (upsert, encrypt, [SensitiveData])

**Files:**
- Create: `src/DeveloperPlatform.Application/Secrets/SetSecret/SetSecretCommand.cs`
- Create: `src/DeveloperPlatform.Infrastructure/Secrets/SetSecretCommandHandler.cs`
- Modify: `ServiceCollectionExtensions.cs`
- Create: `src/DeveloperPlatform.Api/Endpoints/Secrets/SecretsEndpoints.cs` (+ `Program.cs`)
- Test: `SecretTests.cs`

**Interfaces:**
- Consumes: `ISecretRepository`, `ITenantCryptoService`, `IExecutionContext`.
- Produces: `SetSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name, string Value) : ICommand<Unit>, IResourceScoped` with `[SensitiveData]` on `Value` and `ResourceScope => Scope.Environment(EnvironmentId)`.

- [ ] **Step 1: Write the failing test** (append to `SecretTests.cs`)
```csharp
    [Fact]
    public async Task Set_Then_Set_Overwrites_And_Encrypts()
    {
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant); await _db.SaveChangesAsync();
        var handler = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(
            new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db), crypto,
            new TestExecutionContext { TenantId = _tenant });

        await handler.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "API_KEY", "first"));
        await _db.SaveChangesAsync();
        await handler.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "API_KEY", "second"));
        await _db.SaveChangesAsync();

        var stored = await _db.Secrets.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("second", System.Text.Encoding.UTF8.GetString(stored.EncryptedValue));
        Assert.Equal("second", await crypto.DecryptAsync(_tenant, stored.EncryptedValue, stored.KeyId));
    }
```

- [ ] **Step 2: Run test to verify it fails** — FAIL (command/handler missing).

- [ ] **Step 3: Create the command**

`SetSecret/SetSecretCommand.cs`:
```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.SetSecret;

[RequiresPermission(Permission.SecretsWrite)]
public record SetSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name, [property: SensitiveData] string Value)
    : ICommand<Unit>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}
```
(`[property: SensitiveData]` targets the record's generated property so the scrubber's `GetCustomAttribute<SensitiveDataAttribute>()` finds it.)

- [ ] **Step 4: Create the handler** (upsert + size guard)

`Secrets/SetSecretCommandHandler.cs`:
```csharp
using System.Text;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.SetSecret;
using DeveloperPlatform.Domain.Secrets;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class SetSecretCommandHandler(
    ISecretRepository repository, ITenantCryptoService crypto, IExecutionContext ctx)
    : ICommandHandler<SetSecretCommand, Unit>
{
    private const int MaxValueBytes = 64 * 1024;

    public async Task<Unit> HandleAsync(SetSecretCommand command, CancellationToken ct = default)
    {
        if (Encoding.UTF8.GetByteCount(command.Value) > MaxValueBytes)
        {
            throw new ArgumentException($"Secret value exceeds {MaxValueBytes} bytes.");
        }

        var (payload, keyId) = await crypto.EncryptAsync(ctx.TenantId, command.Value, ct);
        var existing = await repository.GetAsync(command.EnvironmentId, command.Name, ct);
        if (existing is null)
        {
            await repository.AddAsync(
                Secret.Create(ctx.TenantId, command.ProjectId, command.EnvironmentId, command.Name, payload, keyId), ct);
        }
        else
        {
            existing.UpdateValue(payload, keyId);
        }

        return Unit.Value;
    }
}
```

- [ ] **Step 5: DI** — add to a `// Secrets (Slice B)` block:
```csharp
services.AddScoped<ISecretRepository, SecretRepository>();
services.AddScoped<ICommandHandler<SetSecretCommand, Unit>, SetSecretCommandHandler>();
```
Add the `using` imports.

- [ ] **Step 6: Create the endpoints file with the PUT route**

`src/DeveloperPlatform.Api/Endpoints/Secrets/SecretsEndpoints.cs`:
```csharp
using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Secrets.SetSecret;
using Microsoft.AspNetCore.Mvc;

namespace DeveloperPlatform.Api.Endpoints.Secrets;

public static class SecretsEndpoints
{
    public static IEndpointRouteBuilder MapSecrets(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v1/projects/{projectId:guid}/environments/{environmentId:guid}/secrets")
            .WithTags("Secrets").WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        group.MapPut("/{name}", async (Guid projectId, Guid environmentId, string name,
            [FromBody] SetSecretRequest req, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(projectId, environmentId, name, req.Value), ct);
            return Results.NoContent();
        }).WithName("SetSecret").Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    public record SetSecretRequest(string Value);
}
```
Register in `Program.cs` after `app.MapEnvironments(versionSet);`: `app.MapSecrets(versionSet);` + `using DeveloperPlatform.Api.Endpoints.Secrets;`.

- [ ] **Step 7: Run tests / build** — `dotnet test ... --filter SecretTests` PASS; `dotnet build` 0 errors.

- [ ] **Step 8: Commit**
```bash
git add src/DeveloperPlatform.Application/Secrets/SetSecret src/DeveloperPlatform.Infrastructure/Secrets/SetSecretCommandHandler.cs \
        src/DeveloperPlatform.Infrastructure/ServiceCollectionExtensions.cs \
        src/DeveloperPlatform.Api/Endpoints/Secrets src/DeveloperPlatform.Api/Program.cs \
        tests/DeveloperPlatform.Api.Tests/Secrets/SecretTests.cs
git commit -m "feat(secrets): SetSecret upsert with encryption + redacted audit value"
```

### Task B3: ListSecrets (names + metadata, no values)

**Files:** `Application/Secrets/ListSecrets/ListSecretsQuery.cs`, `Infrastructure/Secrets/ListSecretsQueryHandler.cs`, DI, `SecretsEndpoints.cs` (GET), test.

**Interfaces:**
- Produces: `ListSecretsQuery(Guid ProjectId, Guid EnvironmentId) : IQuery<IReadOnlyList<SecretSummary>>, IResourceScoped`; `SecretSummary(string Name, DateTime CreatedAt, DateTime UpdatedAt)`.

- [ ] **Step 1: Failing test** (append to `SecretTests.cs`)
```csharp
    [Fact]
    public async Task List_Returns_Names_And_Meta_Only()
    {
        _db.Secrets.Add(Secret.Create(_tenant, _project, _env, "A", new byte[] { 1 }, Guid.NewGuid()));
        _db.Secrets.Add(Secret.Create(_tenant, _project, _env, "B", new byte[] { 2 }, Guid.NewGuid()));
        await _db.SaveChangesAsync();
        var handler = new DeveloperPlatform.Infrastructure.Secrets.ListSecretsQueryHandler(_db);
        var list = await handler.HandleAsync(
            new DeveloperPlatform.Application.Secrets.ListSecrets.ListSecretsQuery(_project, _env));
        Assert.Equal(new[] { "A", "B" }, list.Select(s => s.Name));
    }
```

- [ ] **Step 2: Run — FAIL.**

- [ ] **Step 3: Query** `ListSecrets/ListSecretsQuery.cs`:
```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.ListSecrets;

[RequiresPermission(Permission.SecretsRead)]
public record ListSecretsQuery(Guid ProjectId, Guid EnvironmentId)
    : IQuery<IReadOnlyList<SecretSummary>>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}

public record SecretSummary(string Name, DateTime CreatedAt, DateTime UpdatedAt);
```

- [ ] **Step 4: Handler** `Secrets/ListSecretsQueryHandler.cs`:
```csharp
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Application.Secrets.ListSecrets;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class ListSecretsQueryHandler(ApplicationDbContext db)
    : IQueryHandler<ListSecretsQuery, IReadOnlyList<SecretSummary>>
{
    public async Task<IReadOnlyList<SecretSummary>> HandleAsync(ListSecretsQuery query, CancellationToken ct = default)
        => await db.Secrets.AsNoTracking()
            .Where(s => s.EnvironmentId == query.EnvironmentId)
            .OrderBy(s => s.Name)
            .Select(s => new SecretSummary(s.Name, s.CreatedAt, s.UpdatedAt))
            .ToListAsync(ct);
}
```

- [ ] **Step 5: DI** `services.AddScoped<IQueryHandler<ListSecretsQuery, IReadOnlyList<SecretSummary>>, ListSecretsQueryHandler>();`

- [ ] **Step 6: Endpoint** — add to `SecretsEndpoints.cs`:
```csharp
        group.MapGet("/", async (Guid projectId, Guid environmentId, IQueryDispatcher d, CancellationToken ct) =>
        {
            var results = await d.SendAsync<ListSecretsQuery, IReadOnlyList<SecretSummary>>(
                new ListSecretsQuery(projectId, environmentId), ct);
            return Results.Ok(results.Select(s => new SecretResponse(s.Name, s.CreatedAt, s.UpdatedAt)));
        }).WithName("ListSecrets").Produces<IEnumerable<SecretResponse>>();
```
Add `public record SecretResponse(string Name, DateTime CreatedAt, DateTime UpdatedAt);` and the query `using`s + `using DeveloperPlatform.Application.Queries;`.

- [ ] **Step 7: Run/build — PASS.**

- [ ] **Step 8: Commit** `git commit -m "feat(secrets): ListSecrets (names + metadata, no values)"`

### Task B4: RevealSecret (audited command, decrypt)

**Files:** `Application/Secrets/RevealSecret/RevealSecretCommand.cs`, `Infrastructure/Secrets/RevealSecretCommandHandler.cs`, DI, `SecretsEndpoints.cs` (POST reveal), test (incl. audit assertion via dispatcher harness).

**Interfaces:**
- Produces: `RevealSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name) : ICommand<RevealSecretResult>, IResourceScoped`; `RevealSecretResult(string Name, string Value)`.

- [ ] **Step 1: Failing test** — two tests: handler decrypts, and a dispatcher-level test proving the reveal is audited (an `AuditOutboxEntries` row is written). Append to `SecretTests.cs`:
```csharp
    [Fact]
    public async Task Reveal_Returns_Original_Plaintext()
    {
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant); await _db.SaveChangesAsync();
        var (payload, keyId) = await crypto.EncryptAsync(_tenant, "sesame");
        _db.Secrets.Add(Secret.Create(_tenant, _project, _env, "PW", payload, keyId));
        await _db.SaveChangesAsync();

        var handler = new DeveloperPlatform.Infrastructure.Secrets.RevealSecretCommandHandler(
            new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db), crypto,
            new TestExecutionContext { TenantId = _tenant });
        var result = await handler.HandleAsync(
            new DeveloperPlatform.Application.Secrets.RevealSecret.RevealSecretCommand(_project, _env, "PW"));
        Assert.Equal("sesame", result.Value);
    }
```
Add a dispatcher-level audit test in a new `tests/DeveloperPlatform.Api.Tests/Secrets/SecretAuthorizationTests.cs` (built in Task B6) — note here that reveal auditing is asserted there.

- [ ] **Step 2: Run — FAIL.**

- [ ] **Step 3: Command** `RevealSecret/RevealSecretCommand.cs`:
```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.RevealSecret;

[RequiresPermission(Permission.SecretsRead)]
public record RevealSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name)
    : ICommand<RevealSecretResult>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}

public record RevealSecretResult(string Name, string Value);
```
(No value field on the command → nothing sensitive to scrub; the audit entry records the reveal by name.)

- [ ] **Step 4: Handler** `Secrets/RevealSecretCommandHandler.cs`:
```csharp
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.RevealSecret;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class RevealSecretCommandHandler(
    ISecretRepository repository, ITenantCryptoService crypto, IExecutionContext ctx)
    : ICommandHandler<RevealSecretCommand, RevealSecretResult>
{
    public async Task<RevealSecretResult> HandleAsync(RevealSecretCommand command, CancellationToken ct = default)
    {
        var secret = await repository.GetAsync(command.EnvironmentId, command.Name, ct)
            ?? throw new KeyNotFoundException($"Secret '{command.Name}' not found.");
        var value = await crypto.DecryptAsync(ctx.TenantId, secret.EncryptedValue, secret.KeyId, ct);
        return new RevealSecretResult(secret.Name, value);
    }
}
```

- [ ] **Step 5: DI** `services.AddScoped<ICommandHandler<RevealSecretCommand, RevealSecretResult>, RevealSecretCommandHandler>();`

- [ ] **Step 6: Endpoint** — add to `SecretsEndpoints.cs`:
```csharp
        group.MapPost("/{name}/reveal", async (Guid projectId, Guid environmentId, string name, ICommandDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync<RevealSecretCommand, RevealSecretResult>(
                new RevealSecretCommand(projectId, environmentId, name), ct);
            return Results.Ok(new RevealResponse(result.Name, result.Value));
        }).WithName("RevealSecret").Produces<RevealResponse>();
```
Add `public record RevealResponse(string Name, string Value);` and the `using`s.

- [ ] **Step 7: Run/build — PASS.**

- [ ] **Step 8: Commit** `git commit -m "feat(secrets): RevealSecret as audited command (decrypts value)"`

### Task B5: DeleteSecret

**Files:** `Application/Secrets/DeleteSecret/DeleteSecretCommand.cs`, `Infrastructure/Secrets/DeleteSecretCommandHandler.cs`, DI, `SecretsEndpoints.cs` (DELETE), test.

- [ ] **Step 1: Failing test** (append to `SecretTests.cs`)
```csharp
    [Fact]
    public async Task Delete_Removes_Secret_And_404_When_Absent()
    {
        _db.Secrets.Add(Secret.Create(_tenant, _project, _env, "X", new byte[] { 1 }, Guid.NewGuid()));
        await _db.SaveChangesAsync();
        var handler = new DeveloperPlatform.Infrastructure.Secrets.DeleteSecretCommandHandler(
            new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db));
        await handler.HandleAsync(new DeveloperPlatform.Application.Secrets.DeleteSecret.DeleteSecretCommand(_project, _env, "X"));
        await _db.SaveChangesAsync();
        Assert.Empty(await _db.Secrets.ToListAsync());
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.HandleAsync(new DeveloperPlatform.Application.Secrets.DeleteSecret.DeleteSecretCommand(_project, _env, "X")));
    }
```

- [ ] **Step 2: Run — FAIL.**

- [ ] **Step 3: Command** `DeleteSecret/DeleteSecretCommand.cs`:
```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.DeleteSecret;

[RequiresPermission(Permission.SecretsWrite)]
public record DeleteSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name)
    : ICommand<Unit>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}
```

- [ ] **Step 4: Handler** `Secrets/DeleteSecretCommandHandler.cs`:
```csharp
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Secrets.DeleteSecret;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class DeleteSecretCommandHandler(ISecretRepository repository)
    : ICommandHandler<DeleteSecretCommand, Unit>
{
    public async Task<Unit> HandleAsync(DeleteSecretCommand command, CancellationToken ct = default)
    {
        var secret = await repository.GetAsync(command.EnvironmentId, command.Name, ct)
            ?? throw new KeyNotFoundException($"Secret '{command.Name}' not found.");
        repository.Delete(secret);
        return Unit.Value;
    }
}
```

- [ ] **Step 5: DI** `services.AddScoped<ICommandHandler<DeleteSecretCommand, Unit>, DeleteSecretCommandHandler>();`

- [ ] **Step 6: Endpoint** — add to `SecretsEndpoints.cs`:
```csharp
        group.MapDelete("/{name}", async (Guid projectId, Guid environmentId, string name, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<DeleteSecretCommand, Unit>(new DeleteSecretCommand(projectId, environmentId, name), ct);
            return Results.NoContent();
        }).WithName("DeleteSecret").Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
```

- [ ] **Step 7: Run/build — PASS.**

- [ ] **Step 8: Commit** `git commit -m "feat(secrets): DeleteSecret"`

### Task B6: Authorization + audit + scrub tests (dispatcher-level)

**Files:** Create `tests/DeveloperPlatform.Api.Tests/Secrets/SecretAuthorizationTests.cs`.

Proves the cross-cutting behavior the spec requires: env-scoped grant allows; no/insufficient grant denies (403); the reveal writes an audit entry; the `SetSecret` value is redacted in the audit payload.

- [ ] **Step 1: Write the tests** — build a real `CommandDispatcher` (copy `EnforcementTests.Build`, registering the secret handlers + `SecretRepository` + `TenantCryptoService`), seed a key, and:
```csharp
    [Fact]
    public async Task Set_Allowed_With_Environment_Grant()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsWrite, Scope.Environment(_env)));
        await _db.SaveChangesAsync();
        await Build().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "v"));
        Assert.Single(await _db.Secrets.ToListAsync());
    }

    [Fact]
    public async Task Set_Forbidden_Without_Grant()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Build().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "v")));
    }

    [Fact]
    public async Task Set_Audit_Redacts_Value()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsWrite, Scope.Environment(_env)));
        await _db.SaveChangesAsync();
        await Build().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "supersecret"));

        var entry = await _db.AuditOutboxEntries.AsNoTracking().SingleAsync();
        var plaintext = await new TenantCryptoService(_db, Key).DecryptAsync(_tenant, entry.EncryptedPayload, entry.KeyId);
        Assert.DoesNotContain("supersecret", plaintext);
        Assert.Contains("[REDACTED]", plaintext);
    }

    [Fact]
    public async Task Reveal_Writes_Audit_Entry()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsWrite, Scope.Environment(_env)));
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsRead, Scope.Environment(_env)));
        await _db.SaveChangesAsync();
        await Build().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "v"));
        await Build().SendAsync<RevealSecretCommand, RevealSecretResult>(new RevealSecretCommand(_project, _env, "K"));

        var types = await _db.AuditOutboxEntries.AsNoTracking().Select(e => e.CommandType).ToListAsync();
        Assert.Contains(nameof(RevealSecretCommand), types);
    }
```
`AuditOutboxEntry` exposes `CommandType`, `EncryptedPayload`, and `KeyId` (verified). The dispatcher's `Build()` must register `ISecretRepository`, the `Set`/`Reveal` handlers, and construct `TenantCryptoService(_db, Key)`.

- [ ] **Step 2: Run — FAIL then implement nothing new (behaviour already exists); adjust the harness until green.**

- [ ] **Step 3: Run/build — PASS.** Run the whole suite: `dotnet test developer-platform-reference.slnx` (stop any running API first to free DLL locks).

- [ ] **Step 4: Commit** `git commit -m "test(secrets): env-scoped authz, reveal auditing, value redaction"`

---

## Slice C — Key rotation

### Task C1: RotateTenantKey (re-encrypt all secrets, retain old keys)

**Files:** `Application/Secrets/RotateTenantKey/RotateTenantKeyCommand.cs`, `Infrastructure/Secrets/RotateTenantKeyCommandHandler.cs`, DI, `SecretsEndpoints.cs` (a second top-level group), `Program.cs`, test `tests/DeveloperPlatform.Api.Tests/Secrets/RotationTests.cs`.

**Interfaces:**
- Produces: `RotateTenantKeyCommand() : ICommand<RotateTenantKeyResult>, IResourceScoped` (`Scope.Tenant`); `RotateTenantKeyResult(int SecretsReEncrypted)`.

- [ ] **Step 1: Failing test** — `RotationTests.cs` (standard harness + `Key`):
```csharp
    [Fact]
    public async Task Rotate_ReEncrypts_All_Secrets_To_New_Key_And_Values_Preserved()
    {
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant); await _db.SaveChangesAsync();
        var (p1, k1) = await crypto.EncryptAsync(_tenant, "one");
        _db.Secrets.Add(Secret.Create(_tenant, _project, _env, "A", p1, k1));
        await _db.SaveChangesAsync();

        var handler = new DeveloperPlatform.Infrastructure.Secrets.RotateTenantKeyCommandHandler(
            _db, crypto, new TestExecutionContext { TenantId = _tenant });
        var result = await handler.HandleAsync(new DeveloperPlatform.Application.Secrets.RotateTenantKey.RotateTenantKeyCommand());
        await _db.SaveChangesAsync();

        Assert.Equal(1, result.SecretsReEncrypted);
        var s = await _db.Secrets.AsNoTracking().SingleAsync();
        Assert.NotEqual(k1, s.KeyId);                                   // new key
        Assert.Equal("one", await crypto.DecryptAsync(_tenant, s.EncryptedValue, s.KeyId));  // value preserved
        Assert.Equal("one", await crypto.DecryptAsync(_tenant, p1, k1)); // old key retained → still decrypts
    }
```

- [ ] **Step 2: Run — FAIL.**

- [ ] **Step 3: Command** `RotateTenantKey/RotateTenantKeyCommand.cs`:
```csharp
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.RotateTenantKey;

[RequiresPermission(Permission.SecretsWrite)]
public record RotateTenantKeyCommand : ICommand<RotateTenantKeyResult>, IResourceScoped
{
    public Scope ResourceScope => Scope.Tenant;
}

public record RotateTenantKeyResult(int SecretsReEncrypted);
```

- [ ] **Step 4: Handler** `Secrets/RotateTenantKeyCommandHandler.cs` (persist new key before re-encrypting):
```csharp
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.RotateTenantKey;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class RotateTenantKeyCommandHandler(
    ApplicationDbContext db, ITenantCryptoService crypto, IExecutionContext ctx)
    : ICommandHandler<RotateTenantKeyCommand, RotateTenantKeyResult>
{
    public async Task<RotateTenantKeyResult> HandleAsync(RotateTenantKeyCommand command, CancellationToken ct = default)
    {
        // Add a new key and flush so GetActiveKeyAsync (a DB query) selects it as the newest.
        await crypto.CreateKeyAsync(ctx.TenantId, ct);
        await db.SaveChangesAsync(ct);

        var secrets = await db.Secrets.ToListAsync(ct);   // tenant filter already applied
        foreach (var secret in secrets)
        {
            var plaintext = await crypto.DecryptAsync(ctx.TenantId, secret.EncryptedValue, secret.KeyId, ct);
            var (payload, keyId) = await crypto.EncryptAsync(ctx.TenantId, plaintext, ct);
            secret.UpdateValue(payload, keyId);
        }

        return new RotateTenantKeyResult(secrets.Count);
    }
}
```
(Old keys are never shredded — `ShredKeyAsync` is not called — so historical audit payloads keep decrypting.)

- [ ] **Step 5: DI** `services.AddScoped<ICommandHandler<RotateTenantKeyCommand, RotateTenantKeyResult>, RotateTenantKeyCommandHandler>();`

- [ ] **Step 6: Endpoint** — add a second group to `SecretsEndpoints.cs` (or a new `MapSecretsAdmin`) inside `MapSecrets`:
```csharp
        var admin = app.MapGroup("/api/v1/secrets")
            .WithTags("Secrets").WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();
        admin.MapPost("/rotate-key", async (ICommandDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync<RotateTenantKeyCommand, RotateTenantKeyResult>(new RotateTenantKeyCommand(), ct);
            return Results.Ok(new RotateKeyResponse(result.SecretsReEncrypted));
        }).WithName("RotateTenantKey").Produces<RotateKeyResponse>();
```
Add `public record RotateKeyResponse(int SecretsReEncrypted);` and the `using`.

- [ ] **Step 7: Run/build — PASS.**

- [ ] **Step 8: Commit** `git commit -m "feat(secrets): tenant key rotation with re-encryption (old keys retained)"`

---

## Slice D — Web UI

> Blazor pages are verified by build + manual Playwright walkthrough (the repo does not unit-test components; only `DeveloperPlatformApiClient` is unit-tested). Follow the existing MudBlazor zinc theme and the globally-interactive render mode (no per-page `@rendermode`). Reuse patterns from `ServiceAccounts.razor` / `ManageKeysDialog.razor` (clipboard + snackbar).

### Task D1: API client methods + DTOs (unit-tested)

**Files:** Modify `src/DeveloperPlatform.Web/Http/DeveloperPlatformApiClient.cs`; add DTOs to `Http/Models/` (new `SecretDtos.cs`); test `tests/DeveloperPlatform.Web.Tests/Http/DeveloperPlatformApiClientTests.cs`.

**Interfaces (produce these client methods):**
```csharp
Task<IReadOnlyList<EnvironmentDto>> GetEnvironmentsAsync(Guid projectId);
Task<Guid> CreateEnvironmentAsync(Guid projectId, string name, string type);
Task RenameEnvironmentAsync(Guid projectId, Guid environmentId, string name);
Task DeleteEnvironmentAsync(Guid projectId, Guid environmentId);
Task<IReadOnlyList<SecretDto>> GetSecretsAsync(Guid projectId, Guid environmentId);
Task SetSecretAsync(Guid projectId, Guid environmentId, string name, string value);
Task<string> RevealSecretAsync(Guid projectId, Guid environmentId, string name);
Task DeleteSecretAsync(Guid projectId, Guid environmentId, string name);
Task<int> RotateKeyAsync();
```
DTOs (`SecretDtos.cs`): `record EnvironmentDto(Guid Id, string Name, string Type, DateTime CreatedAt);`, `record SecretDto(string Name, DateTime CreatedAt, DateTime UpdatedAt);`, `record RevealDto(string Name, string Value);`, `record RotateKeyDto(int SecretsReEncrypted);`.

- [ ] **Step 1:** Write a client unit test mirroring the existing `DeveloperPlatformApiClientTests` (a stubbed `HttpMessageHandler` returning canned JSON) asserting `GetSecretsAsync` deserializes names and `RevealSecretAsync` returns the value. **Step 2:** run → FAIL. **Step 3:** implement the methods (mirror the existing `GetServiceAccountsAsync`/`IssueApiKeyAsync` shape; routes exactly as in Slices A–C). **Step 4:** run → PASS. **Step 5:** commit `feat(web): API client for environments + secrets`.

### Task D2: Project detail page — environments + secrets

**Files:** Create `src/DeveloperPlatform.Web/Components/Pages/ProjectDetail.razor` (`@page "/projects/{ProjectId:guid}"`); modify `Projects.razor` so a row links to `/projects/{id}`.

- [ ] Build a page with: a `MudTabs` strip bound to `GetEnvironmentsAsync`; per tab a `MudDataGrid` of `GetSecretsAsync` (columns Name, Updated, actions); an **Add secret** button opening `SecretDialog`; per-row **Reveal** (calls `RevealSecretAsync`, shows value masked with a copy button — reuse `ManageKeysDialog` clipboard+snackbar), **Edit** (opens `SecretDialog` prefilled name), **Delete** (confirm → `DeleteSecretAsync`).
- Create `SecretDialog.razor` (name + value `InputType.Password` with reveal toggle; submit → `SetSecretAsync`).
- [ ] Verify: `dotnet build`; then manual Playwright walkthrough — create secret, list, reveal+copy, edit, delete at desktop and 390px.
- [ ] Commit `feat(web): project detail with per-environment secrets management`.

### Task D3: Environment management UI

**Files:** Create `EnvironmentDialog.razor`; add controls to `ProjectDetail.razor`.

- [ ] Add "New environment" (name + type `MudSelect` over `Development/Staging/Production`) → `CreateEnvironmentAsync`; per-tab rename (→ `RenameEnvironmentAsync`) and delete (typed confirm warning secrets are destroyed → `DeleteEnvironmentAsync`, then refresh tabs).
- [ ] Verify build + manual walkthrough. Commit `feat(web): environment management on project detail`.

### Task D4: Rotate encryption key UI

**Files:** Add a control to a tenant/settings surface (e.g., a new `Settings.razor` at `/settings`, linked in `NavMenu.razor`), Owner/Admin only.

- [ ] A "Rotate encryption key" button with a typed confirm → `RotateKeyAsync()`, reporting the re-encrypted count via snackbar. Gate visibility on the caller holding tenant-scope `secrets:write` (reuse the existing permission-check pattern the other pages use; if none exists client-side, simply show the control and let the API 403 with a friendly snackbar).
- [ ] Verify build + manual walkthrough. Commit `feat(web): rotate encryption key control`.

---

## Final verification (before finishing the branch)

- [ ] Stop any running API/Web (they lock shared DLLs), then `dotnet test developer-platform-reference.slnx` — all suites green (Web, Architecture, Api).
- [ ] Apply the migration against the dev DB (`dotnet ef database update ...` or let the app migrate on startup) and run the full stack; manual Playwright pass over the secrets flow end-to-end (human via Web UI and a service-account API key revealing a secret).
- [ ] Use superpowers:finishing-a-development-branch.

## Out of scope (YAGNI)

Secret value history/rollback; binary secrets; `.env` import/export; secret references/templating; per-secret ACLs; scheduled rotation; hard key-shredding on rotation.
