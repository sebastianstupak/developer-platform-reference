using System.Net;
using DeveloperPlatform.Web.Http;

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
}
