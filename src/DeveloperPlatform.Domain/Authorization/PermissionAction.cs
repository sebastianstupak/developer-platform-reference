namespace DeveloperPlatform.Domain.Authorization;

// Named PermissionAction (not Action) to avoid colliding with System.Action under ImplicitUsings.
public enum PermissionAction
{
    Read,
    Write,
    Manage,
    Delete,
}
