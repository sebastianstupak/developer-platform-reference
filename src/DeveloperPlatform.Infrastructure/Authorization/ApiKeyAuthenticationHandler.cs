using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeveloperPlatform.Infrastructure.Authorization;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApplicationDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string KeyPrefixMarker = "dpk_";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (!header.StartsWith(bearer, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }
        var key = header[bearer.Length..].Trim();
        if (!key.StartsWith(KeyPrefixMarker, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var resolved = await ResolveCredentialAsync(db, key, DateTime.UtcNow);
        if (resolved is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var claims = new[]
        {
            new Claim("tenant_id", resolved.Value.TenantId.ToString()),
            new Claim("principal_id", resolved.Value.PrincipalId.ToString()),
            new Claim("principal_type", nameof(PrincipalType.ServiceAccount)),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    // Looks a key up by its SHA-256 hash, IGNORING the tenant query filter (the key determines the tenant),
    // and returns the owning service-account principal + tenant if the credential is active.
    public static async Task<(Guid PrincipalId, Guid TenantId)?> ResolveCredentialAsync(
        ApplicationDbContext db, string presentedKey, DateTime nowUtc, CancellationToken ct = default)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey)));
        var credential = await db.ApiKeyCredentials
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.KeyHash == hash, ct);
        if (credential is null || !credential.IsActive(nowUtc))
        {
            return null;
        }

        credential.RecordUsage();
        await db.SaveChangesAsync(ct);
        return (credential.ServiceAccountId, credential.TenantId);
    }
}
