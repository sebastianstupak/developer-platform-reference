namespace DeveloperPlatform.Domain.Authorization;

// Optional override for the wire token of a Resource/PermissionAction member.
// Used only when the derived (lowercased identifier) token is not desired,
// e.g. [Token("service-accounts")] on a multi-word member.
[AttributeUsage(AttributeTargets.Field)]
public sealed class TokenAttribute(string token) : Attribute
{
    public string Token { get; } = token;
}
