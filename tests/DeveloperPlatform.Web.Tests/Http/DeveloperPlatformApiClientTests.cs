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

    // Captures the outgoing request URI so query-string construction can be asserted.
    private sealed class CapturingHandler(string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
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

    // --- Audit log (Slice B) ---

    [Fact]
    public async Task GetAuditEventsAsync_Deserializes_Items_And_Total_On_200()
    {
        var page = new
        {
            items = new[]
            {
                new
                {
                    id = Guid.NewGuid(),
                    occurredAt = DateTime.UtcNow,
                    commandType = "CreateProject",
                    status = "Success",
                    actorDisplay = "alice@example.com",
                    principalType = "Member",
                    ipAddress = "127.0.0.1",
                    isCrossTenant = false,
                    projectId = (Guid?)null,
                    environmentId = (Guid?)null,
                },
            },
            total = 42,
            page = 1,
            pageSize = 20,
        };
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.OK, JsonSerializer.Serialize(page));
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var filter = new AuditFilterDto(null, null, [], [], [], null);

        var result = await client.GetAuditEventsAsync(filter, 1, 20);

        Assert.Single(result.Items);
        Assert.Equal(42, result.Total);
        Assert.Equal("CreateProject", result.Items[0].CommandType);
    }

    [Fact]
    public async Task GetAuditEventsAsync_Emits_Repeated_Params_For_Multi_Value_Filters()
    {
        var capture = new CapturingHandler("{\"items\":[],\"total\":0,\"page\":1,\"pageSize\":25}");
        var client = new DeveloperPlatformApiClient(
            new HttpClient(capture) { BaseAddress = new Uri("http://localhost") });
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var filter = new AuditFilterDto(null, null, [a, b],
            ["SetSecretCommand", "RevealSecretCommand"], ["Success", "Failed"], true);

        await client.GetAuditEventsAsync(filter, 1, 25);

        var query = capture.LastRequestUri!.Query;
        Assert.Contains($"principalId={a}", query);
        Assert.Contains($"principalId={b}", query);
        Assert.Contains("commandType=SetSecretCommand", query);
        Assert.Contains("commandType=RevealSecretCommand", query);
        Assert.Contains("status=Success", query);
        Assert.Contains("status=Failed", query);
        Assert.Contains("crossTenantOnly=True", query);
    }

    [Fact]
    public async Task GetAuditEventsAsync_Emits_ProjectId_Param_When_Set()
    {
        var capture = new CapturingHandler("{\"items\":[],\"total\":0,\"page\":1,\"pageSize\":8}");
        var client = new DeveloperPlatformApiClient(
            new HttpClient(capture) { BaseAddress = new Uri("http://localhost") });
        var project = Guid.NewGuid();
        var filter = new AuditFilterDto(null, null, [], [], [], null, project);

        await client.GetAuditEventsAsync(filter, 1, 8);

        Assert.Contains($"projectId={project}", capture.LastRequestUri!.Query);
    }

    [Fact]
    public async Task GetAuditEventsAsync_Returns_Empty_Page_On_Failure()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, string.Empty);
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var filter = new AuditFilterDto(null, null, [], [], [], null);

        var result = await client.GetAuditEventsAsync(filter, 1, 20);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public async Task GetAuditEventDetailAsync_Returns_Detail_On_200()
    {
        var detail = new
        {
            @event = new
            {
                id = Guid.NewGuid(),
                occurredAt = DateTime.UtcNow,
                commandType = "DeleteSecret",
                status = "Failed",
                actorDisplay = (string?)null,
                principalType = "ServiceAccount",
                ipAddress = "10.0.0.5",
                isCrossTenant = true,
                projectId = Guid.NewGuid(),
                environmentId = Guid.NewGuid(),
            },
            crossTenantReason = "support-access",
            payloadJson = "{\"key\":\"value\"}",
            payloadAvailable = true,
        };
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.OK, JsonSerializer.Serialize(detail));
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var result = await client.GetAuditEventDetailAsync(Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Equal("DeleteSecret", result!.Event.CommandType);
        Assert.Equal("support-access", result.CrossTenantReason);
        Assert.True(result.PayloadAvailable);
    }

    [Fact]
    public async Task GetAuditEventDetailAsync_Returns_Null_On_Failure()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, string.Empty);
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var result = await client.GetAuditEventDetailAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAuditCommandTypesAsync_Returns_List_On_200()
    {
        var types = new[] { "CreateProject", "DeleteSecret" };
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.OK, JsonSerializer.Serialize(types));
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var result = await client.GetAuditCommandTypesAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains("CreateProject", result);
    }

    [Fact]
    public async Task GetAuditCommandTypesAsync_Returns_Empty_On_Failure()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, string.Empty);
        var client = new DeveloperPlatformApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var result = await client.GetAuditCommandTypesAsync();

        Assert.Empty(result);
    }
}
