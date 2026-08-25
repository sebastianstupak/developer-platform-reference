using DeveloperPlatform.Application.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Infrastructure.Context;

public sealed class ExecutionContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, HttpExecutionContext executionContext)
    {
        var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value
            ?? throw new UnauthorizedAccessException("tenant_id claim is required.");

        if (!Guid.TryParse(tenantClaim, out var tenantId))
        {
            throw new UnauthorizedAccessException("tenant_id claim is not a valid GUID.");
        }

        executionContext.TenantId = tenantId;
        executionContext.IpAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (Guid.TryParse(httpContext.User.FindFirst("project_id")?.Value, out var projectId))
        {
            executionContext.ProjectId = projectId;
        }

        if (Guid.TryParse(httpContext.User.FindFirst("environment_id")?.Value, out var envId))
        {
            executionContext.EnvironmentId = envId;
        }

        var resolver = httpContext.RequestServices.GetRequiredService<IPrincipalResolver>();
        var resolved = await resolver.ResolveAsync(httpContext.User, tenantId, httpContext.RequestAborted);
        if (resolved is not null)
        {
            executionContext.PrincipalId = resolved.PrincipalId;
            executionContext.PrincipalType = resolved.Type;
            executionContext.UserId = resolved.UserId;
        }

        await next(httpContext);
    }
}
