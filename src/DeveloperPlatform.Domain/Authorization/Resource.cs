namespace DeveloperPlatform.Domain.Authorization;

public enum Resource
{
    Projects,
    Secrets,
    [Token("api-keys")]
    ApiKeys,
    Members,
    Roles,
    [Token("service-accounts")]
    ServiceAccounts,
    Audit,
}
