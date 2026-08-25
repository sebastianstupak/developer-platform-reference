namespace DeveloperPlatform.Domain.Authorization;

// Join row: a permission belonging to a role. Composite key (RoleId, Permission).
public class RolePermission
{
    public Guid RoleId { get; private set; }
    public Permission Permission { get; private set; }

    private RolePermission() { }

    public static RolePermission Create(Guid roleId, Permission permission) =>
        new() { RoleId = roleId, Permission = permission };
}
