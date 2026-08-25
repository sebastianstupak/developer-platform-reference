using DeveloperPlatform.Domain.ApiKeys;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class ApiKeyCredentialTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Sa = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ApiKeyCredential New(DateTime? expiresAt) =>
        ApiKeyCredential.Create(Tenant, Sa, "ci", "dpk_abc12345", "HASH", expiresAt);

    [Fact]
    public void Create_Sets_Fields_And_Requires_Name()
    {
        var c = New(null);
        Assert.Equal(Sa, c.ServiceAccountId);
        Assert.Equal("dpk_abc12345", c.KeyPrefix);
        Assert.Equal("HASH", c.KeyHash);
        Assert.False(c.IsRevoked);
        Assert.Throws<ArgumentException>(() => ApiKeyCredential.Create(Tenant, Sa, " ", "p", "h", null));
    }

    [Fact]
    public void IsActive_Honours_Revocation_And_Expiry()
    {
        Assert.True(New(null).IsActive(Now));                       // no expiry, not revoked
        Assert.True(New(Now.AddDays(1)).IsActive(Now));             // not yet expired
        Assert.False(New(Now.AddDays(-1)).IsActive(Now));           // expired

        var revoked = New(null);
        revoked.Revoke();
        Assert.True(revoked.IsRevoked);
        Assert.NotNull(revoked.RevokedAt);
        Assert.False(revoked.IsActive(Now));
    }

    [Fact]
    public void RecordUsage_Sets_LastUsedAt()
    {
        var c = New(null);
        Assert.Null(c.LastUsedAt);
        c.RecordUsage();
        Assert.NotNull(c.LastUsedAt);
    }
}
