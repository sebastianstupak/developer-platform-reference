using Microsoft.AspNetCore.Authentication;

namespace DeveloperPlatform.Web.Http;

internal sealed class ApiTokenHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TokenProvider _tokenProvider;

    public ApiTokenHandler(IHttpContextAccessor httpContextAccessor, TokenProvider tokenProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // TokenProvider is populated via PersistentComponentState during Blazor circuit init.
        // Fall back to HttpContext for the SSR prerender phase.
        var token = _tokenProvider.AccessToken;

        if (token is null)
        {
            var ctx = _httpContextAccessor.HttpContext;
            if (ctx is not null)
            {
                token = await ctx.GetTokenAsync("access_token");
            }
        }

        if (token is not null)
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
