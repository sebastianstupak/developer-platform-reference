namespace DeveloperPlatform.Web.Http.Models;

// Access-management DTOs mirroring the API JSON. Enums/tokens arrive as strings
// (the API registers a global JsonStringEnumConverter).

public record RoleDto(Guid Id, string Name, IReadOnlyList<string> Permissions);

public record MemberDto(Guid PrincipalId, Guid UserId, string Email, string DisplayName, string Status);

public record InvitationDto(Guid Id, string Email, Guid RoleId, string Status, DateTime ExpiresAt);

public record ServiceAccountDto(Guid PrincipalId, string Name, string? Description, DateTime CreatedAt);

public record ApiKeyDto(
    Guid Id, string Name, string KeyPrefix, DateTime? ExpiresAt,
    bool IsRevoked, DateTime? LastUsedAt, DateTime CreatedAt);

public record PermissionDto(string Token, string Resource, string Action, string Description);

// The plaintext key is returned once, when a key is issued.
public record IssuedKeyDto(Guid CredentialId, string PlaintextKey, string KeyPrefix);
