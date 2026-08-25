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

namespace DeveloperPlatform.Api.Tests.Projects;

public sealed class ProjectsAuthorizationTests : IClassFixture<ProjectsAuthorizationTests.DevPlatformFactory>
{
    private readonly DevPlatformFactory _factory;

    public ProjectsAuthorizationTests(DevPlatformFactory factory)
        => _factory = factory;

    [Fact]
    public async Task GetProjects_Returns_401_Without_Auth()
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/api/v1/projects");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateProject_Returns_401_Without_Auth()
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new { Name = "test" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteProject_Returns_401_Without_Auth()
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.DeleteAsync(
            $"/api/v1/projects/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
                    opts.UseInMemoryDatabase("projects-auth-tests")
                        .ConfigureWarnings(w =>
                            w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

                services.RemoveAll<IHostedService>();
            });
        }
    }
}
