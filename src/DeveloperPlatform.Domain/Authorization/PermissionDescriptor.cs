namespace DeveloperPlatform.Domain.Authorization;

public sealed record PermissionDescriptor(
    Permission Permission,
    Resource Resource,
    PermissionAction Action,
    string Token,
    string Description);
