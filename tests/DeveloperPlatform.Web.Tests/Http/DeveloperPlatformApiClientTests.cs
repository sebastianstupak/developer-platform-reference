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
}
