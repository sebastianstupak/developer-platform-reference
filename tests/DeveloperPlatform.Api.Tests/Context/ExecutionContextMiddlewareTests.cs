using System.Security.Claims;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Infrastructure.Context;
using Microsoft.AspNetCore.Http;

namespace DeveloperPlatform.Api.Tests.Context;

public class ExecutionContextMiddlewareTests
{
    [Fact]
    public async Task Middleware_Populates_TenantId_From_Claim()
    {
        var tenantId = Guid.NewGuid();
        var ctx = new HttpExecutionContext();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("sub", Guid.NewGuid().ToString())
        ]));
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        httpContext.RequestServices = new FakeServiceProvider(ctx);

        var middleware = new ExecutionContextMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(httpContext, ctx);

        Assert.Equal(tenantId, ctx.TenantId);
    }

    [Fact]
    public async Task Middleware_Throws_When_TenantId_Claim_Missing()
    {
        var ctx = new HttpExecutionContext();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", Guid.NewGuid().ToString())
        ]));

        var middleware = new ExecutionContextMiddleware(_ => Task.CompletedTask);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => middleware.InvokeAsync(httpContext, ctx));
    }

    private sealed class FakeServiceProvider(HttpExecutionContext ctx) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(HttpExecutionContext) ? ctx : null;
    }
}
