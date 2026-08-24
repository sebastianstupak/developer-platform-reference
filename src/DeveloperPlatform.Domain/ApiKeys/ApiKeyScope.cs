namespace DeveloperPlatform.Domain.ApiKeys;

[Flags]
public enum ApiKeyScope
{
    None = 0,
    Read = 1 << 0,
    Write = 1 << 1,
    Admin = 1 << 2,
}
