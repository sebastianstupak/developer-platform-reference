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

namespace DeveloperPlatform.Api.Tests.Secrets;

public sealed class SecretVersionEndpointsTests : IClassFixture<SecretVersionEndpointsTests.DevPlatformFactory>
{
    private readonly DevPlatformFactory _factory;
    public SecretVersionEndpointsTests(DevPlatformFactory factory) => _factory = factory;

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static string Base(Guid p, Guid e, string name) => $"/api/v1/projects/{p}/environments/{e}/secrets/{name}";

    [Fact]
    public async Task ListVersions_Returns_401_Without_Auth()
    {
        var r = await Client().GetAsync($"{Base(Guid.NewGuid(), Guid.NewGuid(), "K")}/versions");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task RevealVersion_Returns_401_Without_Auth()
    {
        var r = await Client().PostAsync($"{Base(Guid.NewGuid(), Guid.NewGuid(), "K")}/versions/1/reveal", null);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Rollback_Returns_401_Without_Auth()
    {
        var r = await Client().PostAsJsonAsync($"{Base(Guid.NewGuid(), Guid.NewGuid(), "K")}/rollback", new { version = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    public sealed class DevPlatformFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.AddDbContext<ApplicationDbContext>((sp, opts) =>
                    opts.UseInMemoryDatabase("secret-version-endpoint-tests")
                        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                services.RemoveAll<IHostedService>();
            });
        }
    }
}
