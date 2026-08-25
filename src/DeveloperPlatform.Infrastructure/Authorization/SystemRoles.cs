using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Infrastructure.Authorization;

// Deterministic definitions for the built-in system roles, used for HasData seeding and later reuse.
public static class SystemRoles
{
    // Fixed ids + a fixed timestamp so HasData seed data is stable across migrations.
    public static readonly Guid OwnerId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AdminId = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid DeveloperId = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid ViewerId = new("44444444-4444-4444-4444-444444444444");

    public static readonly DateTime SeededAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Permission[] AllPerms = Enum.GetValues<Permission>();

    private static readonly Permission[] AdminPerms =
        AllPerms.Where(p => p != Permission.RolesManage).ToArray();

    private static readonly Permission[] DeveloperPerms =
    [
        Permission.ProjectsRead, Permission.ProjectsWrite,
        Permission.SecretsRead, Permission.SecretsWrite,
    ];

    private static readonly Permission[] ViewerPerms =
    [
        Permission.ProjectsRead, Permission.SecretsRead, Permission.AuditRead,
    ];

    public static IReadOnlyList<Role> All { get; } =
    [
        Role.CreateSystem(OwnerId, "Owner", SeededAt),
        Role.CreateSystem(AdminId, "Admin", SeededAt),
        Role.CreateSystem(DeveloperId, "Developer", SeededAt),
        Role.CreateSystem(ViewerId, "Viewer", SeededAt),
    ];

    public static IReadOnlyList<RolePermission> AllPermissions { get; } =
    [
        .. AllPerms.Select(p => RolePermission.Create(OwnerId, p)),
        .. AdminPerms.Select(p => RolePermission.Create(AdminId, p)),
        .. DeveloperPerms.Select(p => RolePermission.Create(DeveloperId, p)),
        .. ViewerPerms.Select(p => RolePermission.Create(ViewerId, p)),
    ];
}
