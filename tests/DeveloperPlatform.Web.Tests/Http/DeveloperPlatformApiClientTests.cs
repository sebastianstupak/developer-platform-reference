using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DeveloperPlatform.Web.Http;
using DeveloperPlatform.Web.Http.Models;

namespace DeveloperPlatform.Web.Tests.Http;

public sealed class DeveloperPlatformApiClientTests
{
    [Fact]
    public async Task IsHealthyAsync_Returns_True_When_Api_Returns_200()
    {
        // Arrange
        var client = CreateClientWithResponse(HttpStatusCode.OK);
        var apiClient = new DeveloperPlatformApiClient(client);

        // Act
        var result = await apiClient.IsHealthyAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsHealthyAsync_Returns_False_When_Api_Returns_503()
    {
        // Arrange
        var client = CreateClientWithResponse(HttpStatusCode.ServiceUnavailable);
        var apiClient = new DeveloperPlatformApiClient(client);

        // Act
        var result = await apiClient.IsHealthyAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsHealthyAsync_Returns_False_When_Api_Throws()
    {
        // Arrange
        var handler = new ThrowingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://api/") };
        var apiClient = new DeveloperPlatformApiClient(client);

        // Act
        var result = await apiClient.IsHealthyAsync();

        // Assert
        Assert.False(result);
    }

    private static HttpClient CreateClientWithResponse(HttpStatusCode statusCode)
    {
        var handler = new StaticResponseHandler(statusCode);
        return new HttpClient(handler) { BaseAddress = new Uri("http://api/") };
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StaticResponseHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_statusCode));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Connection refused");
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public MockHttpMessageHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task GetProjectsAsync_Returns_List_On_200()
    {
        var projects = new[]
        {
            new { id = Guid.NewGuid(), name = "Alpha", description = (string?)null, createdAt = DateTime.UtcNow },
        };
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.OK, JsonSerializer.Serialize(projects));
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var result = await client.GetProjectsAsync();

        Assert.Single(result);
        Assert.Equal("Alpha", result[0].Name);
    }

    [Fact]
    public async Task CreateProjectAsync_Returns_ProjectId_On_201()
    {
        var created = new { projectId = Guid.NewGuid() };
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.Created, JsonSerializer.Serialize(created));
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var result = await client.CreateProjectAsync("Beta", null);

        Assert.Equal(created.projectId, result);
    }

    [Fact]
    public async Task DeleteProjectAsync_Returns_True_On_204()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NoContent, string.Empty);
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var result = await client.DeleteProjectAsync(Guid.NewGuid());

        Assert.True(result);
    }

    // --- Environments & Secrets (Slice D) ---

    [Fact]
    public async Task GetEnvironmentsAsync_Returns_List_On_200()
    {
        var environments = new[]
        {
            new { id = Guid.NewGuid(), name = "Production", type = "Production", createdAt = DateTime.UtcNow },
        };
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.OK, JsonSerializer.Serialize(environments));
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var result = await client.GetEnvironmentsAsync(Guid.NewGuid());

        Assert.Single(result);
        Assert.Equal("Production", result[0].Name);
    }

    [Fact]
    public async Task CreateEnvironmentAsync_Returns_EnvironmentId_On_201()
    {
        var created = new { environmentId = Guid.NewGuid() };
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.Created, JsonSerializer.Serialize(created));
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var result = await client.CreateEnvironmentAsync(Guid.NewGuid(), "Staging", "Staging");

        Assert.Equal(created.environmentId, result);
    }

    [Fact]
    public async Task RenameEnvironmentAsync_Succeeds_On_204()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NoContent, string.Empty);
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        await client.RenameEnvironmentAsync(Guid.NewGuid(), Guid.NewGuid(), "Renamed");
    }

    [Fact]
    public async Task DeleteEnvironmentAsync_Succeeds_On_204()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NoContent, string.Empty);
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        await client.DeleteEnvironmentAsync(Guid.NewGuid(), Guid.NewGuid());
    }

    [Fact]
    public async Task GetSecretsAsync_Deserializes_Names_On_200()
    {
        var secrets = new[]
        {
            new { name = "DATABASE_URL", createdAt = DateTime.UtcNow, updatedAt = DateTime.UtcNow },
            new { name = "API_KEY", createdAt = DateTime.UtcNow, updatedAt = DateTime.UtcNow },
        };
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.OK, JsonSerializer.Serialize(secrets));
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var result = await client.GetSecretsAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(2, result.Count);
        Assert.Equal("DATABASE_URL", result[0].Name);
        Assert.Equal("API_KEY", result[1].Name);
    }

    [Fact]
    public async Task SetSecretAsync_Succeeds_On_204()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NoContent, string.Empty);
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        await client.SetSecretAsync(Guid.NewGuid(), Guid.NewGuid(), "DATABASE_URL", "postgres://...");
    }

    [Fact]
    public async Task RevealSecretAsync_Returns_Value_On_200()
    {
        var revealed = new { name = "DATABASE_URL", value = "postgres://secret" };
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.OK, JsonSerializer.Serialize(revealed));
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var result = await client.RevealSecretAsync(Guid.NewGuid(), Guid.NewGuid(), "DATABASE_URL");

        Assert.Equal("postgres://secret", result);
    }

    [Fact]
    public async Task DeleteSecretAsync_Succeeds_On_204()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NoContent, string.Empty);
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        await client.DeleteSecretAsync(Guid.NewGuid(), Guid.NewGuid(), "DATABASE_URL");
    }

    [Fact]
    public async Task RotateKeyAsync_Returns_SecretsReEncrypted_On_200()
    {
        var rotated = new { secretsReEncrypted = 7 };
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.OK, JsonSerializer.Serialize(rotated));
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var result = await client.RotateKeyAsync();

        Assert.Equal(7, result);
    }
}
