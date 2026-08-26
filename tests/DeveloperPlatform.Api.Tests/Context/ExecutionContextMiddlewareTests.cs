using System.Security.Claims;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Authorization;
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

    [Fact]
    public async Task Middleware_Ignores_Spoofed_PrincipalId_When_Not_ApiKey_Scheme()
    {
        var tenantId = Guid.NewGuid();
        var spoofedPrincipalId = Guid.NewGuid();
        var resolvedPrincipalId = Guid.NewGuid();
        var ctx = new HttpExecutionContext();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("principal_id", spoofedPrincipalId.ToString()),
            new Claim("sub", Guid.NewGuid().ToString())
        ], "Bearer"));
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        httpContext.RequestServices = new FakeServiceProvider(ctx,
            new StubResolver(new ResolvedPrincipal(resolvedPrincipalId, PrincipalType.Member, Guid.NewGuid())));

        var middleware = new ExecutionContextMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(httpContext, ctx);

        Assert.Equal(resolvedPrincipalId, ctx.PrincipalId);
        Assert.NotEqual(spoofedPrincipalId, ctx.PrincipalId);
        Assert.Equal(PrincipalType.Member, ctx.PrincipalType);
    }

    [Fact]
    public async Task Middleware_Trusts_PrincipalId_When_ApiKey_Scheme()
    {
        var tenantId = Guid.NewGuid();
        var principalId = Guid.NewGuid();
        var ctx = new HttpExecutionContext();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("principal_id", principalId.ToString())
        ], ApiKeyAuthenticationHandler.SchemeName));
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        httpContext.RequestServices = new FakeServiceProvider(ctx, new ThrowingResolver());

        var middleware = new ExecutionContextMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(httpContext, ctx);

        Assert.Equal(principalId, ctx.PrincipalId);
        Assert.Equal(PrincipalType.ServiceAccount, ctx.PrincipalType);
    }

    private sealed class ThrowingResolver : IPrincipalResolver
    {
        public Task<ResolvedPrincipal?> ResolveAsync(ClaimsPrincipal user, Guid tenantId, CancellationToken ct = default)
            => throw new InvalidOperationException("Resolver should not be consulted for ApiKey-scheme callers.");
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
