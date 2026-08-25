namespace DeveloperPlatform.Web.Http;

/// <summary>
/// Circuit-scoped store for the OIDC access token.
/// Populated during SSR via PersistentComponentState and restored during
/// the Blazor Server circuit so that ApiTokenHandler can forward the token.
/// </summary>
public sealed class TokenProvider
{
    public string? AccessToken { get; set; }
}
