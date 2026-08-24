using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace DeveloperPlatform.Infrastructure.Context;

public sealed class ExecutionContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, HttpExecutionContext executionContext)
    {
        var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value
            ?? throw new UnauthorizedAccessException("tenant_id claim is required.");

        if (!Guid.TryParse(tenantClaim, out var tenantId))
            throw new UnauthorizedAccessException("tenant_id claim is not a valid GUID.");

        executionContext.TenantId = tenantId;
        executionContext.IpAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (Guid.TryParse(httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? httpContext.User.FindFirst("sub")?.Value, out var userId))
            executionContext.UserId = userId;

        if (Guid.TryParse(httpContext.User.FindFirst("api_key_id")?.Value, out var apiKeyId))
            executionContext.ApiKeyId = apiKeyId;

        if (Guid.TryParse(httpContext.User.FindFirst("project_id")?.Value, out var projectId))
            executionContext.ProjectId = projectId;

        if (Guid.TryParse(httpContext.User.FindFirst("environment_id")?.Value, out var envId))
            executionContext.EnvironmentId = envId;

        await next(httpContext);
    }
}
