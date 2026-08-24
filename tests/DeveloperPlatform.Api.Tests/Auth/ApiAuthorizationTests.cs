using System.Net;
using System.Net.Http.Json;
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

public sealed class ApiAuthorizationTests : IClassFixture<ApiAuthorizationTests.DevPlatformFactory>
{
    private readonly DevPlatformFactory _factory;

    public ApiAuthorizationTests(DevPlatformFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Health_Returns_200_Without_Auth()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateApiKey_Returns_401_Without_Auth()
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync(
            "/api/v1/projects/00000000-0000-0000-0000-000000000001/api-keys",
            new { Name = "test", Scopes = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public sealed class DevPlatformFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                // Replace MySQL with InMemory so the test host starts without a DB
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.AddDbContext<ApplicationDbContext>((sp, opts) =>
                    opts.UseInMemoryDatabase("api-auth-tests")
                        .ConfigureWarnings(w =>
                            w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

                // Suppress RabbitMQ background worker so it doesn't fail without a broker
                services.RemoveAll<IHostedService>();
            });
        }
    }
}
