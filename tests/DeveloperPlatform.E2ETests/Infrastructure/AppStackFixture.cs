using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Infrastructure.Context;
using DeveloperPlatform.Infrastructure.Persistence;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MariaDb;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace DeveloperPlatform.E2ETests.Infrastructure;

public sealed class AppStackFixture : IAsyncLifetime
{
    public const string TenantId = "00000000-0000-0000-0000-000000000001";
    // 127.0.0.1 (not localhost): on Linux CI `localhost` can resolve to IPv6 ::1 first, which the
    // IPv4-bound Keycloak container port isn't listening on — the app's OIDC discovery fetch then fails.
    public string KeycloakBaseUrl => "http://127.0.0.1:8090";
    public string WebBaseUrl => "http://localhost:5000";
    public string ApiBaseUrl => "http://127.0.0.1:5274";

    private readonly MariaDbContainer _db = new MariaDbBuilder("mariadb:11")
        .WithPortBinding(3306, 3306)
        .WithDatabase("developer_platform")
        .WithUsername("app").WithPassword("app")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine")
        .WithPortBinding(6379, 6379)
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:3-management")
        .WithPortBinding(5672, 5672)
        .WithUsername("guest").WithPassword("guest")
        .Build();

    private IContainer _keycloak = default!;
    private AppProcess _api = default!;
    private AppProcess _web = default!;
    private Microsoft.Playwright.IPlaywright _playwright = default!;

    public string DbConnectionString => _db.GetConnectionString();

    public StackSeeder Seeder { get; private set; } = default!;

    public Microsoft.Playwright.IBrowser Browser { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        Console.WriteLine("[e2e-fixture] InitializeAsync begin");
        var repoRoot = CommonDirectoryPath.GetSolutionDirectory().DirectoryPath;
        var realm = Path.Combine(repoRoot, "infra", "keycloak", "realm-export.json");

        _keycloak = new ContainerBuilder("quay.io/keycloak/keycloak:26.2")
            .WithPortBinding(8090, 8080)
            // Target is a DIRECTORY — Testcontainers appends the source file name, so the realm lands
            // at /opt/keycloak/data/import/realm-export.json (not nested inside a dir of that name).
            .WithResourceMapping(new FileInfo(realm), "/opt/keycloak/data/import")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
            .WithCommand("start-dev", "--import-realm")
            // Wait on KC's own startup log line rather than an HTTP probe: the HTTP wait is flaky
            // with fixed port bindings + KC dev-mode hostname handling, while this line is emitted
            // only once the server is listening and the realm import has finished.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Listening on: http://0.0.0.0:8080"))
            .Build();

        // Start containers sequentially with per-container ceilings so a hang names the culprit.
        await Phase("db-start", TimeSpan.FromMinutes(3), () => _db.StartAsync());
        await Phase("redis-start", TimeSpan.FromMinutes(2), () => _redis.StartAsync());
        await Phase("rabbit-start", TimeSpan.FromMinutes(3), () => _rabbit.StartAsync());
        try
        {
            await Phase("keycloak-start", TimeSpan.FromMinutes(5), () => _keycloak.StartAsync());
        }
        catch
        {
            await DumpKeycloakLogsAsync();
            throw;
        }

        await Phase("kc-discovery-probe", TimeSpan.FromMinutes(1), async () =>
        {
            using var http = new HttpClient();
            var probe = await http.GetAsync(
                $"{KeycloakBaseUrl}/realms/developer-platform/.well-known/openid-configuration");
            Console.WriteLine($"[e2e-fixture] KC discovery probe: {(int)probe.StatusCode} {probe.StatusCode} at {KeycloakBaseUrl}");
            // Fail fast here (realm imported + reachable) rather than surfacing much later at seed/login.
            probe.EnsureSuccessStatusCode();
        });

        await Phase("migrate", TimeSpan.FromMinutes(3), async () =>
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseMySql(DbConnectionString, ServerVersion.AutoDetect(DbConnectionString))
                .Options;
            await using var db = new ApplicationDbContext(options, new HttpExecutionContext(), TenancyMode.SharedTables);
            await db.Database.MigrateAsync();
        });

        var apiProj = Path.Combine(repoRoot, "src", "DeveloperPlatform.Api", "DeveloperPlatform.Api.csproj");
        var webProj = Path.Combine(repoRoot, "src", "DeveloperPlatform.Web", "DeveloperPlatform.Web.csproj");

        await Phase("api-launch", TimeSpan.FromMinutes(4), async () =>
        {
            _api = AppProcess.Start(apiProj, new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ASPNETCORE_URLS"] = "http://127.0.0.1:5274",
                ["ConnectionStrings__Default"] = DbConnectionString,
                ["Keycloak__Authority"] = $"{KeycloakBaseUrl}/realms/developer-platform",
            });
            await AppProcess.WaitUntilReadyAsync($"{ApiBaseUrl}/health", TimeSpan.FromMinutes(3));
        });

        await Phase("web-launch", TimeSpan.FromMinutes(4), async () =>
        {
            _web = AppProcess.Start(webProj, new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ASPNETCORE_URLS"] = "http://localhost:5000",
                ["ConnectionStrings__Redis"] = "127.0.0.1:6379",
                ["Api__BaseUrl"] = ApiBaseUrl,
                ["Keycloak__Authority"] = $"{KeycloakBaseUrl}/realms/developer-platform",
                ["Keycloak__ClientSecret"] = "web-client-secret",
            });
            await AppProcess.WaitUntilReadyAsync($"{WebBaseUrl}/", TimeSpan.FromMinutes(3));
        });

        await Phase("playwright-launch", TimeSpan.FromMinutes(3), async () =>
        {
            Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
            _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            Browser = await _playwright.Chromium.LaunchAsync(
                new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true });
        });

        await Phase("seed", TimeSpan.FromMinutes(3), () =>
        {
            Seeder = new StackSeeder(KeycloakBaseUrl, ApiBaseUrl);
            return Seeder.SeedAsync();
        });
        Console.WriteLine("[e2e-fixture] InitializeAsync complete");
    }

    // Runs one startup phase with a hard ceiling so a hang fails fast, naming the phase,
    // instead of silently consuming the whole CI job timeout.
    private static async Task Phase(string name, TimeSpan timeout, Func<Task> action)
    {
        Console.WriteLine($"[e2e-fixture] START {name}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var task = action();
        if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
        {
            throw new TimeoutException($"[e2e-fixture] Phase '{name}' hung — exceeded {timeout.TotalSeconds:n0}s");
        }
        await task;
        Console.WriteLine($"[e2e-fixture] END {name} ({sw.Elapsed.TotalSeconds:n0}s)");
    }

    private async Task DumpKeycloakLogsAsync()
    {
        try
        {
            (string stdout, string stderr) = await _keycloak.GetLogsAsync();
            Console.WriteLine("[e2e-fixture] ===== Keycloak STDOUT =====");
            Console.WriteLine(stdout);
            Console.WriteLine("[e2e-fixture] ===== Keycloak STDERR =====");
            Console.WriteLine(stderr);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[e2e-fixture] could not read keycloak logs: {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        // Guarded: any field may be null if InitializeAsync failed partway.
        try
        { await Browser.DisposeAsync(); }
        catch { /* not started */ }
        try
        { _playwright.Dispose(); }
        catch { /* not started */ }
        try
        { await _web.DisposeAsync(); }
        catch { /* not started */ }
        try
        { await _api.DisposeAsync(); }
        catch { /* not started */ }
        try
        { await _keycloak.DisposeAsync(); }
        catch { /* not started */ }
        try
        { await _rabbit.DisposeAsync(); }
        catch { /* not started */ }
        try
        { await _redis.DisposeAsync(); }
        catch { /* not started */ }
        try
        { await _db.DisposeAsync(); }
        catch { /* not started */ }
    }
}
