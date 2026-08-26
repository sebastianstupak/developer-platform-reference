using System.Net;
using System.Net.Http.Headers;
using DeveloperPlatform.Application.ApiKeys.IssueApiKey;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.ApiKeys;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DeveloperPlatform.Api.Tests.Auth;

// Full HTTP-pipeline coverage for RequestExceptionHandler: a KeyNotFoundException raised deep
// in a command handler (missing secret) must surface as 404, not the ASP.NET Core default 500.
public sealed class RequestExceptionMappingTests : IClassFixture<RequestExceptionMappingTests.DevPlatformFactory>
{
    private const string DbName = "request-exception-mapping-tests";
    private readonly DevPlatformFactory _factory;

    public RequestExceptionMappingTests(DevPlatformFactory factory)
        => _factory = factory;

    [Fact]
    public async Task DeleteSecret_On_Missing_Secret_Returns_404()
    {
        var tenant = Guid.NewGuid();
        var environmentId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(DbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ApplicationDbContext(options, new TestExecutionContext { TenantId = tenant }, TenancyMode.SharedTables);

        var sa = Principal.CreateServiceAccount(tenant, "ci-deployer");
        db.Principals.Add(sa);
        db.ServiceAccounts.Add(ServiceAccount.Create(tenant, sa.Id, "ci-deployer", null));
        await db.SaveChangesAsync();

        var issueHandler = new IssueApiKeyCommandHandler(db, new TestExecutionContext { TenantId = tenant });
        var issued = await issueHandler.HandleAsync(new IssueApiKeyCommand(sa.Id, "test-key", null));

        db.PermissionGrants.Add(PermissionGrant.Create(tenant, sa.Id, Permission.SecretsWrite, Scope.Environment(environmentId)));
        await db.SaveChangesAsync();

        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.PlaintextKey);

        var response = await client.DeleteAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/environments/{environmentId}/secrets/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    public sealed class DevPlatformFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                // Replace MySQL with InMemory so the test host starts without a DB.
                // AddDbContext registers its options delegate additively via
                // IDbContextOptionsConfiguration<T> (not a single replaceable descriptor), so
                // without removing it here the original UseMySql(..., ServerVersion.AutoDetect(...))
                // delegate from AddInfrastructure still runs too and hangs/fails trying to reach
                // a real MySQL server for any request that actually resolves ApplicationDbContext
                // (e.g. once authenticated, past the 401-only paths the other WAF tests exercise).
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>((sp, opts) =>
                    opts.UseInMemoryDatabase(DbName)
                        .ConfigureWarnings(w =>
                            w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

                // Suppress RabbitMQ background worker so it doesn't fail without a broker
                services.RemoveAll<IHostedService>();
            });
        }
    }
}
