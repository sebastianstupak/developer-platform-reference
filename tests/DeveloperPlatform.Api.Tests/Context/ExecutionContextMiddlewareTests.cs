using System.Security.Claims;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Context;
using Microsoft.AspNetCore.Http;

namespace DeveloperPlatform.Api.Tests.Context;

public class ExecutionContextMiddlewareTests
{
    [Fact]
    public async Task Middleware_Populates_Tenant_And_Principal()
    {
        var tenantId = Guid.NewGuid();
        var principalId = Guid.NewGuid();
        var ctx = new HttpExecutionContext();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("sub", Guid.NewGuid().ToString())
        ]));
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        httpContext.RequestServices = new FakeServiceProvider(ctx,
            new StubResolver(new ResolvedPrincipal(principalId, PrincipalType.Member, Guid.NewGuid())));

        var middleware = new ExecutionContextMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(httpContext, ctx);

        Assert.Equal(tenantId, ctx.TenantId);
        Assert.Equal(principalId, ctx.PrincipalId);
        Assert.Equal(PrincipalType.Member, ctx.PrincipalType);
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

    private sealed class StubResolver(ResolvedPrincipal? result) : IPrincipalResolver
    {
        public Task<ResolvedPrincipal?> ResolveAsync(ClaimsPrincipal user, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult(result);
    }

    private sealed class FakeServiceProvider(HttpExecutionContext ctx, IPrincipalResolver resolver) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(HttpExecutionContext) ? ctx
            : serviceType == typeof(IPrincipalResolver) ? resolver
            : null;
    }
}
