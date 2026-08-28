# E2E .NET Playwright + Testcontainers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Node `tests/e2e/` suite with a first-class .NET xUnit e2e project that drives the Web UI with Playwright for .NET against a full stack provisioned by Testcontainers.

**Architecture:** A shared xUnit collection fixture (`AppStackFixture`) starts MariaDB / Redis / RabbitMQ / Keycloak as containers on fixed host ports, applies EF migrations, launches the API (`:5274`) and Web (`:5000`) as child `dotnet` processes wired to those endpoints, then seeds sample data by logging in as `dev@example.com` and calling the API. Test classes share that fixture and drive the browser. The suite is tagged `[Trait("Category","E2E")]` and excluded from the normal CI test run.

**Tech Stack:** .NET 10, xUnit, `Microsoft.Playwright`, `Testcontainers` (`.MariaDb`, `.Redis`, `.RabbitMq`, generic Keycloak), Keycloak 26.2, Pomelo/MariaDB.

## Global Constraints

- Target framework: `net10.0`. Test framework: xUnit (match the repo's other test projects).
- Fixed host ports (match `docker-compose.yml` so the app's config "just works"): MariaDB `3306`, RabbitMQ `5672`, Redis `6379`, Keycloak `8090`→container `8080`, API `5274`, Web `5000`.
- Web **must** answer on `http://localhost:5000` and Keycloak on `http://localhost:8090` — the realm hardcodes those redirect URIs. Consequence: one e2e run at a time; stop any manual stack on those ports first.
- Dev tenant id (hardcoded Keycloak claim): `00000000-0000-0000-0000-000000000001`.
- Seed data via the API using a **password-grant token** from the realm's `cli-client` (username `dev@example.com`, password `password`); requires `directAccessGrantsEnabled: true` on `cli-client`.
- App config keys: DB `ConnectionStrings:Default` (Pomelo `UseMySql`), Redis `ConnectionStrings:Redis`, RabbitMQ hostname (default port 5672), Keycloak `Keycloak:Authority`, Web→API `Api:BaseUrl`, Web `Keycloak:ClientSecret=web-client-secret`.
- Every e2e test class carries `[Trait("Category", "E2E")]` and `[Collection("app-stack")]`.
- The normal solution test run uses `--filter "Category!=E2E"`; Docker Desktop is required to run the e2e suite.
- Commit messages: Conventional Commits, **no AI co-author trailers** (repo policy; the `commit-msg` hook rejects them). The `pre-commit` hook builds the whole solution — every task must leave it building.

---

### Task 1: Scaffold the e2e project, wire the solution, exclude from CI

**Files:**
- Create: `tests/DeveloperPlatform.E2ETests/DeveloperPlatform.E2ETests.csproj`
- Create: `tests/DeveloperPlatform.E2ETests/TraitFilterSmokeTests.cs` (temporary; removed in Task 6)
- Modify: `developer-platform-reference.slnx`
- Modify: `.github/workflows/ci.yml:44-45`

**Interfaces:**
- Produces: a buildable test project in the solution, tagged tests skippable via `Category!=E2E`.

- [ ] **Step 1: Create the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\DeveloperPlatform.Infrastructure\DeveloperPlatform.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add the runtime packages (latest stable)**

Run from the project dir:
```bash
cd tests/DeveloperPlatform.E2ETests
dotnet add package Microsoft.Playwright
dotnet add package Testcontainers
dotnet add package Testcontainers.MariaDb
dotnet add package Testcontainers.Redis
dotnet add package Testcontainers.RabbitMq
cd ../..
```

- [ ] **Step 3: Register the project in the solution**

Edit `developer-platform-reference.slnx`, add inside the `/tests/` folder block:
```xml
    <Project Path="tests/DeveloperPlatform.E2ETests/DeveloperPlatform.E2ETests.csproj" />
```

- [ ] **Step 4: Add a temporary trait-filter smoke test**

`tests/DeveloperPlatform.E2ETests/TraitFilterSmokeTests.cs`:
```csharp
namespace DeveloperPlatform.E2ETests;

public class TraitFilterSmokeTests
{
    [Fact]
    [Trait("Category", "E2E")]
    public void E2E_trait_is_present() => Assert.True(true);
}
```

- [ ] **Step 5: Exclude E2E from the CI test step**

In `.github/workflows/ci.yml`, change the Test step command to:
```yaml
      - name: Test
        run: dotnet test developer-platform-reference.slnx --no-build -c Release --verbosity minimal --filter "Category!=E2E"
```

- [ ] **Step 6: Verify the filter mechanics**

```bash
dotnet build developer-platform-reference.slnx
dotnet test developer-platform-reference.slnx --filter "Category!=E2E" --verbosity minimal
```
Expected: solution builds; the run reports the existing suites and **does not** execute `E2E_trait_is_present` (0 tests from the E2E project). Then:
```bash
dotnet test tests/DeveloperPlatform.E2ETests --verbosity minimal
```
Expected: `E2E_trait_is_present` runs and passes.

- [ ] **Step 7: Commit**

```bash
git add tests/DeveloperPlatform.E2ETests developer-platform-reference.slnx .github/workflows/ci.yml
git commit -m "test(e2e): scaffold .NET e2e project, wire solution, exclude from CI"
```

---

### Task 2: Testcontainers infra fixture (DB / Redis / RabbitMQ / Keycloak)

**Files:**
- Create: `tests/DeveloperPlatform.E2ETests/Infrastructure/AppStackFixture.cs`
- Create: `tests/DeveloperPlatform.E2ETests/Infrastructure/AppStackCollection.cs`
- Create: `tests/DeveloperPlatform.E2ETests/StackSmokeTests.cs` (temporary; trimmed in later tasks)

**Interfaces:**
- Produces: `AppStackFixture` exposing `string DbConnectionString`, `string KeycloakBaseUrl` (`http://localhost:8090`), and (later tasks add more). `[CollectionDefinition("app-stack")]` bound to it.

- [ ] **Step 1: Write the collection definition**

`Infrastructure/AppStackCollection.cs`:
```csharp
namespace DeveloperPlatform.E2ETests.Infrastructure;

[CollectionDefinition("app-stack")]
public sealed class AppStackCollection : ICollectionFixture<AppStackFixture>;
```

- [ ] **Step 2: Write the fixture with the four containers**

`Infrastructure/AppStackFixture.cs`:
```csharp
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.MariaDb;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace DeveloperPlatform.E2ETests.Infrastructure;

public sealed class AppStackFixture : IAsyncLifetime
{
    public const string TenantId = "00000000-0000-0000-0000-000000000001";
    public string KeycloakBaseUrl => "http://localhost:8090";
    public string WebBaseUrl => "http://localhost:5000";
    public string ApiBaseUrl => "http://127.0.0.1:5274";

    private readonly MariaDbContainer _db = new MariaDbBuilder()
        .WithImage("mariadb:11")
        .WithPortBinding(3306, 3306)
        .WithDatabase("developer_platform")
        .WithUsername("app").WithPassword("app")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .WithPortBinding(6379, 6379)
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management")
        .WithPortBinding(5672, 5672)
        .WithUsername("guest").WithPassword("guest")
        .Build();

    private IContainer _keycloak = default!;

    public string DbConnectionString => _db.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        var repoRoot = CommonDirectoryPath.GetSolutionDirectory().DirectoryPath;
        var realm = Path.Combine(repoRoot, "infra", "keycloak", "realm-export.json");

        _keycloak = new ContainerBuilder()
            .WithImage("quay.io/keycloak/keycloak:26.2")
            .WithPortBinding(8090, 8080)
            .WithResourceMapping(new FileInfo(realm), "/opt/keycloak/data/import/realm-export.json")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
            .WithCommand("start-dev", "--import-realm")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r
                .ForPath("/realms/developer-platform/.well-known/openid-configuration")
                .ForPort(8080)))
            .Build();

        await Task.WhenAll(
            _db.StartAsync().AsTask(),
            _redis.StartAsync().AsTask(),
            _rabbit.StartAsync().AsTask(),
            _keycloak.StartAsync().AsTask());
    }

    public async ValueTask DisposeAsync()
    {
        await _keycloak.DisposeAsync();
        await _rabbit.DisposeAsync();
        await _redis.DisposeAsync();
        await _db.DisposeAsync();
    }
}
```

- [ ] **Step 3: Write the stack smoke test (fails until Docker/containers work)**

`StackSmokeTests.cs`:
```csharp
using DeveloperPlatform.E2ETests.Infrastructure;

namespace DeveloperPlatform.E2ETests;

[Trait("Category", "E2E")]
[Collection("app-stack")]
public class StackSmokeTests(AppStackFixture stack)
{
    [Fact]
    public async Task Keycloak_discovery_document_is_reachable()
    {
        using var http = new HttpClient();
        var res = await http.GetAsync(
            $"{stack.KeycloakBaseUrl}/realms/developer-platform/.well-known/openid-configuration");
        Assert.True(res.IsSuccessStatusCode);
    }

    [Fact]
    public void Db_connection_string_is_exposed() =>
        Assert.Contains("3306", stack.DbConnectionString);
}
```

- [ ] **Step 4: Remove the temporary Task 1 smoke test**

Delete `tests/DeveloperPlatform.E2ETests/TraitFilterSmokeTests.cs`.

- [ ] **Step 5: Run (Docker Desktop must be running; stop any local stack on the fixed ports first)**

```bash
docker compose down 2>/dev/null
dotnet test tests/DeveloperPlatform.E2ETests --verbosity minimal
```
Expected: containers pull/start; both smoke tests PASS. (First run is slow — image pulls + Keycloak ~30–45s.)

- [ ] **Step 6: Commit**

```bash
git add tests/DeveloperPlatform.E2ETests
git commit -m "test(e2e): Testcontainers stack fixture (db, redis, rabbitmq, keycloak)"
```

---

### Task 3: Apply EF migrations inside the fixture

**Files:**
- Modify: `tests/DeveloperPlatform.E2ETests/Infrastructure/AppStackFixture.cs`
- Modify: `tests/DeveloperPlatform.E2ETests/StackSmokeTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext` and `SystemRoles` from `DeveloperPlatform.Infrastructure`.
- Produces: a migrated schema with the four seeded system roles present after `InitializeAsync`.

- [ ] **Step 1: Add a migration step to the fixture**

In `AppStackFixture.InitializeAsync`, after the `Task.WhenAll(...)` container start, add:
```csharp
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(DbConnectionString, ServerVersion.AutoDetect(DbConnectionString))
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.MigrateAsync();
```
Add usings:
```csharp
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
```
> If `ApplicationDbContext`'s constructor differs (e.g. it needs additional services), use
> `ApplicationDbContextFactory` from `DeveloperPlatform.Infrastructure.Persistence` instead — it
> already builds the context from a connection string. Confirm the exact ctor before writing.

- [ ] **Step 2: Extend the smoke test to prove migrations ran**

Add to `StackSmokeTests`:
```csharp
    [Fact]
    public async Task System_roles_are_seeded_by_migrations()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<
                DeveloperPlatform.Infrastructure.Persistence.ApplicationDbContext>()
            .UseMySql(stack.DbConnectionString,
                Microsoft.EntityFrameworkCore.ServerVersion.AutoDetect(stack.DbConnectionString))
            .Options;
        await using var db = new DeveloperPlatform.Infrastructure.Persistence.ApplicationDbContext(options);
        var ownerExists = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .AnyAsync(db.Roles, r => r.Name == "Owner");
        Assert.True(ownerExists);
    }
```
> Confirm the `DbSet` name (`db.Roles`) and `Role.Name` against `ApplicationDbContext`; adjust if
> the set or property name differs.

- [ ] **Step 3: Run**

```bash
dotnet test tests/DeveloperPlatform.E2ETests --verbosity minimal
```
Expected: all three smoke tests PASS (roles query returns true).

- [ ] **Step 4: Commit**

```bash
git add tests/DeveloperPlatform.E2ETests
git commit -m "test(e2e): apply EF migrations against the db container in the fixture"
```

---

### Task 4: Launch API + Web as child processes with readiness

**Files:**
- Create: `tests/DeveloperPlatform.E2ETests/Infrastructure/AppProcess.cs`
- Modify: `tests/DeveloperPlatform.E2ETests/Infrastructure/AppStackFixture.cs`
- Modify: `tests/DeveloperPlatform.E2ETests/StackSmokeTests.cs`

**Interfaces:**
- Produces: `AppProcess` (a disposable wrapper starting `dotnet run` with env + killing the tree); the fixture launches API then Web and waits for readiness.

- [ ] **Step 1: Write the process wrapper**

`Infrastructure/AppProcess.cs`:
```csharp
using System.Diagnostics;

namespace DeveloperPlatform.E2ETests.Infrastructure;

public sealed class AppProcess : IAsyncDisposable
{
    private readonly Process _process;

    private AppProcess(Process process) => _process = process;

    public static AppProcess Start(string projectPath, IReadOnlyDictionary<string, string> env)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "run", "--project", projectPath, "--no-launch-profile" },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var (k, v) in env) psi.Environment[k] = v;
        var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {projectPath}");
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return new AppProcess(p);
    }

    public static async Task WaitUntilReadyAsync(string url, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var res = await http.GetAsync(url);
                if ((int)res.StatusCode < 500) return;
            }
            catch { /* not up yet */ }
            await Task.Delay(1000);
        }
        throw new TimeoutException($"{url} not ready within {timeout}.");
    }

    public async ValueTask DisposeAsync()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
        await Task.CompletedTask;
        _process.Dispose();
    }
}
```

- [ ] **Step 2: Launch API + Web from the fixture**

Add fields to `AppStackFixture`:
```csharp
    private AppProcess _api = default!;
    private AppProcess _web = default!;
```
At the end of `InitializeAsync` (after migrations), add:
```csharp
        var repoRoot = CommonDirectoryPath.GetSolutionDirectory().DirectoryPath;
        var apiProj = Path.Combine(repoRoot, "src", "DeveloperPlatform.Api", "DeveloperPlatform.Api.csproj");
        var webProj = Path.Combine(repoRoot, "src", "DeveloperPlatform.Web", "DeveloperPlatform.Web.csproj");

        _api = AppProcess.Start(apiProj, new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = "http://localhost:5274",
            ["ConnectionStrings__Default"] = DbConnectionString,
            ["ConnectionStrings__Redis"] = "127.0.0.1:6379",
            ["Keycloak__Authority"] = $"{KeycloakBaseUrl}/realms/developer-platform",
        });
        await AppProcess.WaitUntilReadyAsync($"{ApiBaseUrl}/health", TimeSpan.FromMinutes(2));

        _web = AppProcess.Start(webProj, new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = "http://localhost:5000",
            ["ConnectionStrings__Redis"] = "127.0.0.1:6379",
            ["Api__BaseUrl"] = ApiBaseUrl,
            ["Keycloak__Authority"] = $"{KeycloakBaseUrl}/realms/developer-platform",
            ["Keycloak__ClientSecret"] = "web-client-secret",
        });
        await AppProcess.WaitUntilReadyAsync($"{WebBaseUrl}/", TimeSpan.FromMinutes(2));
```
> RabbitMQ hostname defaults to `localhost:5672` (container bound to 5672), which the app's default
> config already targets; only override if the app reads a non-default RabbitMQ config key — confirm
> the RabbitMQ config key in `ServiceCollectionExtensions`/`Program.cs` and add the matching env var
> if needed.

Update `DisposeAsync` to tear down apps **before** containers:
```csharp
        await _web.DisposeAsync();
        await _api.DisposeAsync();
        await _keycloak.DisposeAsync();
        await _rabbit.DisposeAsync();
        await _redis.DisposeAsync();
        await _db.DisposeAsync();
```

- [ ] **Step 3: Extend the smoke test for the running apps**

Add to `StackSmokeTests`:
```csharp
    [Fact]
    public async Task Api_health_and_web_root_respond()
    {
        using var http = new HttpClient();
        var health = await http.GetAsync($"{stack.ApiBaseUrl}/health");
        Assert.True(health.IsSuccessStatusCode);
        var web = await http.GetAsync($"{stack.WebBaseUrl}/");
        Assert.True((int)web.StatusCode < 500);
    }
```

- [ ] **Step 4: Run**

```bash
dotnet test tests/DeveloperPlatform.E2ETests --verbosity minimal
```
Expected: API `/health` returns healthy and Web root responds; all smoke tests PASS. If Keycloak
token issuer mismatches surface in the API/Web logs, add `.WithEnvironment("KC_HOSTNAME_URL", "http://localhost:8090")`
to the Keycloak container and re-run.

- [ ] **Step 5: Commit**

```bash
git add tests/DeveloperPlatform.E2ETests
git commit -m "test(e2e): launch API and Web as child processes with readiness polling"
```

---

### Task 5: Enable password grant + seed sample data via the API

**Files:**
- Modify: `infra/keycloak/realm-export.json` (cli-client `directAccessGrantsEnabled`)
- Create: `tests/DeveloperPlatform.E2ETests/Infrastructure/StackSeeder.cs`
- Modify: `tests/DeveloperPlatform.E2ETests/Infrastructure/AppStackFixture.cs`
- Modify: `tests/DeveloperPlatform.E2ETests/StackSmokeTests.cs`

**Interfaces:**
- Produces: seeded projects (`payments-api` + `billing-service`), each with `production`/`staging`/`development` environments and one secret, created through the API (audit events generated). Fixture exposes `Task<string> GetAccessTokenAsync()`.

- [ ] **Step 1: Enable Direct Access Grants on `cli-client`**

In `infra/keycloak/realm-export.json`, on the `cli-client` object, set:
```json
"directAccessGrantsEnabled": true,
```
(The property is currently `false`. Leave `web-client` unchanged.)

- [ ] **Step 2: Write the seeder**

`Infrastructure/StackSeeder.cs`:
```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace DeveloperPlatform.E2ETests.Infrastructure;

public sealed class StackSeeder(string keycloakBaseUrl, string apiBaseUrl)
{
    private static readonly string[] EnvNames = ["production", "staging", "development"];
    private static readonly (string Type, string Name)[] Envs =
        [("Production", "production"), ("Staging", "staging"), ("Development", "development")];

    public async Task<string> GetAccessTokenAsync()
    {
        using var http = new HttpClient();
        var res = await http.PostAsync(
            $"{keycloakBaseUrl}/realms/developer-platform/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "cli-client",
                ["username"] = "dev@example.com",
                ["password"] = "password",
                ["scope"] = "openid",
            }));
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<TokenResponse>();
        return body!.access_token;
    }

    public async Task SeedAsync()
    {
        var token = await GetAccessTokenAsync();
        using var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var existing = await http.GetFromJsonAsync<List<ProjectDto>>("/api/v1/projects") ?? [];
        var byName = existing.ToDictionary(p => p.Name, p => p.Id);

        foreach (var name in new[] { "payments-api", "billing-service" })
        {
            if (!byName.TryGetValue(name, out var projectId))
            {
                var created = await http.PostAsJsonAsync("/api/v1/projects",
                    new { name, description = $"{name} (seeded)" });
                created.EnsureSuccessStatusCode();
                projectId = (await created.Content.ReadFromJsonAsync<ProjectDto>())!.Id;
            }

            foreach (var (type, envName) in Envs)
            {
                var env = await http.PostAsJsonAsync(
                    $"/api/v1/projects/{projectId}/environments",
                    new { name = envName, type });
                if (!env.IsSuccessStatusCode) continue; // already exists
                var envId = (await env.Content.ReadFromJsonAsync<EnvironmentDto>())!.Id;
                await http.PostAsJsonAsync(
                    $"/api/v1/projects/{projectId}/environments/{envId}/secrets",
                    new { name = "DATABASE_URL", value = $"mysql://{envName}.db/{name}" });
            }
        }
    }

    private sealed record TokenResponse(string access_token);
    private sealed record ProjectDto(Guid Id, string Name);
    private sealed record EnvironmentDto(Guid Id, string Name);
}
```
> Confirm the exact API routes and request/response shapes against the endpoint files
> (`ProjectsEndpoints.cs`, `EnvironmentsEndpoints.cs`, `SecretsEndpoints.cs`) — the versioned base is
> `/api/v1`. Adjust the `type` casing to match `EnvironmentType` enum names, and the secret request
> field names to `SetSecretRequest`. Fix the DTO property names to match the list/response JSON.

- [ ] **Step 3: Call the seeder from the fixture**

Add to `AppStackFixture`:
```csharp
    public StackSeeder Seeder { get; private set; } = default!;
```
At the end of `InitializeAsync` (after Web is ready):
```csharp
        Seeder = new StackSeeder(KeycloakBaseUrl, ApiBaseUrl);
        await Seeder.SeedAsync();
```

- [ ] **Step 4: Prove seeding worked**

Replace the DB-string smoke assertion with a seeded-data check in `StackSmokeTests`:
```csharp
    [Fact]
    public async Task Seeded_projects_are_visible_via_api()
    {
        var token = await stack.Seeder.GetAccessTokenAsync();
        using var http = new HttpClient { BaseAddress = new Uri(stack.ApiBaseUrl) };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var body = await http.GetStringAsync("/api/v1/projects");
        Assert.Contains("payments-api", body);
    }
```

- [ ] **Step 5: Run**

```bash
dotnet test tests/DeveloperPlatform.E2ETests --verbosity minimal
```
Expected: token acquired, projects/environments/secrets created, `payments-api` visible; smoke tests PASS.

- [ ] **Step 6: Commit**

```bash
git add infra/keycloak/realm-export.json tests/DeveloperPlatform.E2ETests
git commit -m "test(e2e): seed sample data via API (cli-client direct grant) in the fixture"
```

---

### Task 6: Playwright base class, login helper, and first ported test

**Files:**
- Create: `tests/DeveloperPlatform.E2ETests/Infrastructure/E2ETestBase.cs`
- Modify: `tests/DeveloperPlatform.E2ETests/Infrastructure/AppStackFixture.cs` (own the browser)
- Create: `tests/DeveloperPlatform.E2ETests/ProjectsSwitcherTests.cs` (first test only)
- Delete: `tests/DeveloperPlatform.E2ETests/StackSmokeTests.cs`

**Interfaces:**
- Consumes: `AppStackFixture.Browser` (`IBrowser`), `AppStackFixture.WebBaseUrl`.
- Produces: `E2ETestBase` giving each test a `Page` (`IPage`) with BaseURL set, plus `LoginAsync()`.

- [ ] **Step 1: Install the browser + own an `IBrowser` in the fixture**

Add to `AppStackFixture`:
```csharp
    public Microsoft.Playwright.IBrowser Browser { get; private set; } = default!;
    private Microsoft.Playwright.IPlaywright _playwright = default!;
```
In `InitializeAsync`, before seeding:
```csharp
        Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(
            new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true });
```
In `DisposeAsync`, before the app processes:
```csharp
        await Browser.DisposeAsync();
        _playwright.Dispose();
```

- [ ] **Step 2: Write the test base**

`Infrastructure/E2ETestBase.cs`:
```csharp
using Microsoft.Playwright;

namespace DeveloperPlatform.E2ETests.Infrastructure;

[Collection("app-stack")]
public abstract class E2ETestBase(AppStackFixture stack) : IAsyncLifetime
{
    protected AppStackFixture Stack { get; } = stack;
    protected IBrowserContext Context { get; private set; } = default!;
    protected IPage Page { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        Context = await Stack.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Stack.WebBaseUrl,
            ViewportSize = new ViewportSize { Width = 1280, Height = 860 },
        });
        Page = await Context.NewPageAsync();
    }

    public async ValueTask DisposeAsync() => await Context.DisposeAsync();

    protected async Task LoginAsync()
    {
        await Page.GotoAsync("/login");
        await Page.WaitForSelectorAsync("#username", new() { Timeout = 30_000 });
        await Page.FillAsync("#username", "dev@example.com");
        await Page.FillAsync("#password", "password");
        await Page.ClickAsync("#kc-login");
        await Page.WaitForURLAsync("**/", new() { Timeout = 30_000 });
    }
}
```

- [ ] **Step 3: Port the first test**

`ProjectsSwitcherTests.cs`:
```csharp
using DeveloperPlatform.E2ETests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace DeveloperPlatform.E2ETests;

[Trait("Category", "E2E")]
public class ProjectsSwitcherTests(AppStackFixture stack) : E2ETestBase(stack)
{
    [Fact]
    public async Task Projects_render_as_cards()
    {
        await LoginAsync();
        await Page.GotoAsync("/projects");
        await Expect(Page.Locator(".project-card").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.GetByText(new System.Text.RegularExpressions.Regex("environment")).First)
            .ToBeVisibleAsync();
    }
}
```

- [ ] **Step 4: Delete the temporary smoke tests file**

Delete `tests/DeveloperPlatform.E2ETests/StackSmokeTests.cs`.

- [ ] **Step 5: Run**

```bash
dotnet test tests/DeveloperPlatform.E2ETests --filter "FullyQualifiedName~ProjectsSwitcherTests" --verbosity minimal
```
Expected: browser logs in, `/projects` shows a `.project-card`; test PASSES.

- [ ] **Step 6: Commit**

```bash
git add tests/DeveloperPlatform.E2ETests
git commit -m "test(e2e): Playwright base, Keycloak login helper, first ported test"
```

---

### Task 7: Port the rest of the projects-switcher tests

**Files:**
- Modify: `tests/DeveloperPlatform.E2ETests/ProjectsSwitcherTests.cs`
- Create: `tests/DeveloperPlatform.E2ETests/MobileContextDialogTests.cs`

**Interfaces:**
- Consumes: `E2ETestBase`. No new produced interface.

- [ ] **Step 1: Add the overview-navigation and combobox tests**

Append to `ProjectsSwitcherTests`:
```csharp
    [Fact]
    public async Task Card_click_opens_overview_and_env_card_navigates_to_secrets()
    {
        await LoginAsync();
        await Page.GotoAsync("/projects");
        await Expect(Page.Locator(".project-card").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.Locator(".project-card").First.ClickAsync();
        await Page.WaitForURLAsync("**/projects/**");

        await Expect(Page.GetByText("Recent activity", new() { Exact = true })).ToBeVisibleAsync();

        var trigger = Page.Locator(".dp-combobox__trigger").First;
        await Expect(trigger).ToBeVisibleAsync();
        await Expect(trigger).Not.ToContainTextAsync("Select project");

        var envCard = Page.Locator(".env-card").First;
        await Expect(envCard).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await envCard.ClickAsync();

        await Page.WaitForURLAsync("**/environments/**");
        await Expect(Page.GetByText(new System.Text.RegularExpressions.Regex("· secrets"))).ToBeVisibleAsync();
    }

    [Fact]
    public async Task App_bar_combobox_searches_and_switches_projects()
    {
        await LoginAsync();
        await Page.GotoAsync("/projects");
        await Page.Locator(".dp-combobox__trigger").First.ClickAsync();
        var popover = Page.Locator(".dp-combobox-popover.mud-popover-open");
        await Expect(popover).ToBeVisibleAsync();
        await Expect(popover.Locator(".dp-command__search")).ToBeFocusedAsync();

        await popover.Locator(".dp-command__search").FillAsync("payments");
        await popover.Locator(".dp-command__item", new() { HasText = "payments-api" }).First.ClickAsync();
        await Page.WaitForURLAsync("**/projects/**");
        await Expect(Page.Locator(".dp-combobox__trigger").First).ToContainTextAsync("payments-api");
    }
```

- [ ] **Step 2: Add the mobile-dialog test (separate class for the mobile viewport)**

`MobileContextDialogTests.cs`:
```csharp
using DeveloperPlatform.E2ETests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace DeveloperPlatform.E2ETests;

[Trait("Category", "E2E")]
[Collection("app-stack")]
public class MobileContextDialogTests(AppStackFixture stack) : IAsyncLifetime
{
    private IBrowserContext _context = default!;
    private IPage _page = default!;

    public async ValueTask InitializeAsync()
    {
        _context = await stack.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = stack.WebBaseUrl,
            ViewportSize = new ViewportSize { Width = 390, Height = 850 },
        });
        _page = await _context.NewPageAsync();
    }

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task Acronym_button_opens_dialog_to_switch_project()
    {
        await _page.GotoAsync("/login");
        await _page.WaitForSelectorAsync("#username", new() { Timeout = 30_000 });
        await _page.FillAsync("#username", "dev@example.com");
        await _page.FillAsync("#password", "password");
        await _page.ClickAsync("#kc-login");
        await _page.WaitForURLAsync("**/", new() { Timeout = 30_000 });
        await _page.GotoAsync("/projects");

        await _page.Locator(".dp-ctxbtn").ClickAsync();
        await Expect(_page.Locator(".dp-ctxdlg")).ToBeVisibleAsync();
        await _page.Locator(".dp-ctxdlg .dp-command__item", new() { HasText = "payments-api" }).First.ClickAsync();
        await _page.WaitForURLAsync("**/projects/**");
        await Expect(_page.Locator(".dp-ctxbtn")).ToContainTextAsync("payments-api");
    }
}
```

- [ ] **Step 3: Run**

```bash
dotnet test tests/DeveloperPlatform.E2ETests --filter "FullyQualifiedName~ProjectsSwitcherTests|FullyQualifiedName~MobileContextDialogTests" --verbosity minimal
```
Expected: all four switcher/mobile tests PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/DeveloperPlatform.E2ETests
git commit -m "test(e2e): port projects switcher navigation and mobile dialog tests"
```

---

### Task 8: Port the audit-filters tests

**Files:**
- Create: `tests/DeveloperPlatform.E2ETests/AuditFiltersTests.cs`

**Interfaces:**
- Consumes: `E2ETestBase`. Depends on seed data having generated audit events (Task 5).

- [ ] **Step 1: Write the audit-filter tests**

`AuditFiltersTests.cs`:
```csharp
using DeveloperPlatform.E2ETests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace DeveloperPlatform.E2ETests;

[Trait("Category", "E2E")]
public class AuditFiltersTests(AppStackFixture stack) : E2ETestBase(stack)
{
    private const string ActionCell = "tbody tr td:nth-child(3)";
    private const string StatusCell = "tbody tr td:nth-child(4)";

    private async Task GotoAuditAsync()
    {
        await LoginAsync();
        await Page.GotoAsync("/audit");
        await Expect(Page.Locator("tbody tr").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task Action_multiselect_narrows_grid_to_chosen_command_types()
    {
        await GotoAuditAsync();
        string[] wanted = ["RevealSecretCommand", "SetSecretCommand"];
        await Page.GetByLabel("Action", new() { Exact = true }).ClickAsync();
        foreach (var w in wanted)
            await Page.Locator(".mud-list-item", new() { HasText = w }).ClickAsync();
        await Page.Keyboard.PressAsync("Escape");

        await Expect(Page.Locator(ActionCell).First).ToBeVisibleAsync();
        await Expect(async () =>
        {
            var cells = await Page.Locator(ActionCell).AllInnerTextsAsync();
            Assert.True(cells.Count > 0 && cells.All(c => wanted.Contains(c.Trim())));
        }).ToPassAsync();
    }

    [Fact]
    public async Task Status_multiselect_keeps_successes_and_failures()
    {
        await GotoAuditAsync();
        await Page.GetByLabel("Status", new() { Exact = true }).ClickAsync();
        await Page.Locator(".mud-list-item", new() { HasText = "Success" }).ClickAsync();
        await Page.Locator(".mud-list-item", new() { HasText = "Failed" }).ClickAsync();
        await Page.Keyboard.PressAsync("Escape");

        await Expect(async () =>
        {
            var cells = await Page.Locator(StatusCell).AllInnerTextsAsync();
            Assert.True(cells.Count > 0 && cells.All(c => c.Trim() is "Success" or "Failed"));
        }).ToPassAsync();
    }

    [Fact]
    public async Task Actor_search_selects_an_actor_as_a_removable_chip()
    {
        await GotoAuditAsync();
        var actor = Page.GetByLabel("Actor", new() { Exact = true });
        await actor.ClickAsync();
        await actor.FillAsync("dev"); // matches the seeded dev@example.com actor
        await Page.Locator(".mud-list-item").First.ClickAsync();

        var filterChips = Page.Locator(".pa-4 .mud-chip");
        await Expect(filterChips).ToHaveCountAsync(1);
        await Expect(Page.Locator("tbody tr").First).ToBeVisibleAsync();

        await filterChips.First.Locator("button").First.ClickAsync();
        await Expect(filterChips).ToHaveCountAsync(0);
    }
}
```
> The Node test searched the actor by `unknown` (the old `@unknown` seed email). Here the seeded
> actor is `dev@example.com`, so the search term is `dev`. Confirm the actor label/text rendered in
> the audit grid and adjust the search term if the display differs.

- [ ] **Step 2: Run the full e2e suite**

```bash
dotnet test tests/DeveloperPlatform.E2ETests --verbosity minimal
```
Expected: all **7** tests PASS (3 audit + 3 switcher + 1 mobile).

- [ ] **Step 3: Commit**

```bash
git add tests/DeveloperPlatform.E2ETests
git commit -m "test(e2e): port audit log filter tests"
```

---

### Task 9: Remove the Node suite, add a project README, final verification

**Files:**
- Delete: `tests/e2e/package.json`, `package-lock.json`, `playwright.config.js`, `README.md`, `.gitignore`, `tests/*.spec.js`
- Create: `tests/DeveloperPlatform.E2ETests/README.md`

**Interfaces:** none.

- [ ] **Step 1: Remove the tracked Node suite**

```bash
git rm -r tests/e2e/package.json tests/e2e/package-lock.json tests/e2e/playwright.config.js \
  tests/e2e/README.md tests/e2e/.gitignore tests/e2e/tests
```
> Untracked scratch `*.mjs` files under `tests/e2e/` are not tracked and are left for you to delete
> manually (`rm tests/e2e/*.mjs`) if desired.

- [ ] **Step 2: Write the project README**

`tests/DeveloperPlatform.E2ETests/README.md`:
```markdown
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
```

- [ ] **Step 3: Final full verification**

```bash
dotnet build developer-platform-reference.slnx
dotnet test developer-platform-reference.slnx --filter "Category!=E2E" --verbosity minimal   # existing suites, no e2e
dotnet test tests/DeveloperPlatform.E2ETests --verbosity minimal                              # 7 e2e tests pass
```
Expected: build clean; solution test run excludes all e2e; e2e project runs all 7 and passes.

- [ ] **Step 4: Commit**

```bash
git add tests/DeveloperPlatform.E2ETests/README.md
git commit -m "test(e2e): remove Node e2e suite, add .NET e2e README"
```

---

## Self-Review

- **Spec coverage:** new project + solution wiring (Task 1) ✓; Testcontainers fixture with fixed
  ports (Task 2) ✓; migrations (Task 3) ✓; API+Web processes + readiness + config wiring (Task 4) ✓;
  fixture-via-API seeding + realm direct-grant change (Task 5) ✓; Playwright base/login + 7 ported
  tests (Tasks 6–8) ✓; CI trait filter (Task 1) ✓; Node suite removal + README (Task 9) ✓.
- **Placeholder scan:** the three `> Confirm …` notes are explicit verification steps against named
  files (route shapes, DbSet/ctor names, RabbitMQ key, actor label), not vague TODOs — the surrounding
  code is concrete. No "TBD"/"handle edge cases"/"similar to" left.
- **Type consistency:** `AppStackFixture` members (`Browser`, `WebBaseUrl`, `ApiBaseUrl`,
  `KeycloakBaseUrl`, `DbConnectionString`, `Seeder`) are defined in Tasks 2/4/5/6 and consumed with the
  same names in later tasks; `E2ETestBase.Page`/`LoginAsync` are used consistently in Tasks 6–8;
  `StackSeeder.GetAccessTokenAsync`/`SeedAsync` match their call sites.

## Open items to confirm during implementation (flagged inline, not blocking)

1. Exact `/api/v1` route shapes and request/response JSON for projects/environments/secrets.
2. `ApplicationDbContext` constructor vs `ApplicationDbContextFactory` for the migration step.
3. RabbitMQ config key (only needs an env override if it isn't the default `localhost:5672`).
4. Keycloak issuer on the mapped port — add `KC_HOSTNAME_URL` only if token validation complains.
5. Audit-grid actor display text for the `Actor` search term.

---

## Execution addendum — CI-based verification (supersedes local runs)

Decision change: the e2e suite runs in **GitHub Actions**, not locally (avoids the fixed-port clash
with a running dev stack). This changes how tasks are verified, and adds a workflow task.

- **Per-task verification (Tasks 2–8):** local verification is **build-only** — `dotnet build
  tests/DeveloperPlatform.E2ETests` must compile (this validates the Testcontainers/Playwright API
  usage) and the pre-commit hook must pass. The tests are **not run locally** (Docker + fixed ports
  conflict with the dev stack). Runtime behavior is verified by the CI e2e run.
- **Sequencing (walking skeleton):** implement Tasks 2–6 (fixture → migrations → app processes →
  seeding → first test) **plus Task 10 (the workflow)** first, push, and iterate in CI until the
  fixture + first test are green. Only then add Tasks 7–8 (remaining tests). Task 9 (Node removal +
  README) last.
- **Trigger:** the standing trigger is `workflow_dispatch` (manual, per the CI decision). Because a
  `workflow_dispatch` workflow is only dispatchable once present on the default branch, `e2e.yml`
  **also** carries a temporary `push: branches: [test/e2e-dotnet-playwright]` trigger for pre-merge
  iteration; that push filter is removed before merge, leaving `workflow_dispatch` only.

### Task 10: dedicated e2e CI workflow (manual dispatch)

**Files:** Create `.github/workflows/e2e.yml`

- [ ] **Step 1: Write the workflow**

```yaml
name: E2E

on:
  workflow_dispatch:
  push:
    branches: [test/e2e-dotnet-playwright]   # TEMP: pre-merge iteration; remove before merge

concurrency:
  group: e2e-${{ github.ref }}
  cancel-in-progress: true

jobs:
  e2e:
    name: playwright e2e (testcontainers)
    runs-on: ubuntu-latest
    timeout-minutes: 30
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - name: Restore
        run: dotnet restore developer-platform-reference.slnx
      - name: Build e2e project
        run: dotnet build tests/DeveloperPlatform.E2ETests -c Release --no-restore
      - name: Install Playwright browsers
        run: pwsh tests/DeveloperPlatform.E2ETests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
      - name: Run e2e
        run: dotnet test tests/DeveloperPlatform.E2ETests -c Release --no-build --verbosity minimal
```
> `ubuntu-latest` provides a working Docker engine, so Testcontainers runs unmodified. If
> `playwright.ps1` isn't emitted at that path, use the documented Microsoft.Playwright CLI install
> instead (`dotnet tool` / `Microsoft.Playwright.Program`). The fixture binds fixed ports — fine on a
> clean runner (no compose stack).

- [ ] **Step 2: Verify locally (lint only)**

The workflow can't be run locally; confirm it's valid YAML and paths match the project layout. Commit.

```bash
git add .github/workflows/e2e.yml
git commit -m "ci(e2e): manual-dispatch workflow running the Testcontainers e2e suite"
```
