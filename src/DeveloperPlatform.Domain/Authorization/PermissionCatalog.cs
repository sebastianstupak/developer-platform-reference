using System.Reflection;

namespace DeveloperPlatform.Domain.Authorization;

// Reflects over the Permission enum ONCE to build descriptors and the token map.
// Static because it is derived purely from compile-time enum metadata.
public static class PermissionCatalog
{
    // Field order matters: static initializers run in textual declaration order, and
    // AllDescriptors/ByToken are both built from ByPermission. Keep ByPermission first.
    private static readonly IReadOnlyDictionary<Permission, PermissionDescriptor> ByPermission = Build();

    private static readonly IReadOnlyList<PermissionDescriptor> AllDescriptors =
        ByPermission.Values.OrderBy(d => d.Token, StringComparer.Ordinal).ToList();

    private static readonly IReadOnlyDictionary<string, Permission> ByToken =
        ByPermission.Values.ToDictionary(d => d.Token, d => d.Permission);

    public static IReadOnlyList<PermissionDescriptor> All => AllDescriptors;

    public static PermissionDescriptor Describe(Permission permission) => ByPermission[permission];

    public static string ToToken(Permission permission) => ByPermission[permission].Token;

    public static Permission FromToken(string token) =>
        ByToken.TryGetValue(token, out var permission)
            ? permission
            : throw new ArgumentException($"Unknown permission token '{token}'.", nameof(token));

    private static IReadOnlyDictionary<Permission, PermissionDescriptor> Build()
    {
        var map = new Dictionary<Permission, PermissionDescriptor>();

        foreach (var permission in Enum.GetValues<Permission>())
        {
            var field = typeof(Permission).GetField(permission.ToString())!;
            var perm = field.GetCustomAttribute<PermAttribute>()
                ?? throw new InvalidOperationException(
                    $"Permission '{permission}' is missing a [Perm] attribute.");

            var token = $"{TokenOf(perm.Resource)}:{TokenOf(perm.Action)}";
            map[permission] = new PermissionDescriptor(
                permission, perm.Resource, perm.Action, token, perm.Description);
        }

        return map;
    }

    // Wire token for a Resource/PermissionAction member: an explicit [Token] override,
    // else the lowercased enum identifier.
    private static string TokenOf<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var field = typeof(TEnum).GetField(value.ToString())!;
        var overrideToken = field.GetCustomAttribute<TokenAttribute>();
        return overrideToken?.Token ?? value.ToString().ToLowerInvariant();
    }
}
