using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using DeveloperPlatform.Web.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Web.Tests.Http;

public sealed class ApiTokenHandlerTests
{
    [Fact]
    public async Task SendAsync_Sets_Bearer_Token_When_Access_Token_Present()
    {
        // Arrange
        const string token = "test-access-token";

        var authProperties = new AuthenticationProperties();
        authProperties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = token }]);

        var authenticateResult = AuthenticateResult.Success(
            new AuthenticationTicket(
                new ClaimsPrincipal(),
                authProperties,
                "Cookie"));

        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(new FakeAuthenticationService(authenticateResult));

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };

        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        AuthenticationHeaderValue? capturedHeader = null;
        var innerHandler = new FakeDelegatingHandler(request =>
        {
            capturedHeader = request.Headers.Authorization;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new ApiTokenHandler(httpContextAccessor, new TokenProvider())
        {
            InnerHandler = innerHandler
        };

        var invoker = new HttpMessageInvoker(handler);

        // Act
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://api/test"),
            CancellationToken.None);

        // Assert
        Assert.NotNull(capturedHeader);
        Assert.Equal("Bearer", capturedHeader!.Scheme);
        Assert.Equal(token, capturedHeader.Parameter);
    }

    [Fact]
    public async Task SendAsync_Does_Not_Set_Auth_Header_When_No_Token()
    {
        // Arrange
        var httpContextAccessor = new HttpContextAccessor { HttpContext = null };

        AuthenticationHeaderValue? capturedHeader = null;
        var innerHandler = new FakeDelegatingHandler(request =>
        {
            capturedHeader = request.Headers.Authorization;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new ApiTokenHandler(httpContextAccessor, new TokenProvider())
        {
            InnerHandler = innerHandler
        };

        var invoker = new HttpMessageInvoker(handler);

        // Act
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://api/test"),
            CancellationToken.None);

        // Assert
        Assert.Null(capturedHeader);
    }

    private sealed class FakeDelegatingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeDelegatingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        private readonly AuthenticateResult _result;

        public FakeAuthenticationService(AuthenticateResult result) => _result = result;

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
            => Task.FromResult(_result);

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;
    }
}
