using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class RequiresPermissionAttribute(Permission permission) : Attribute
{
    public Permission Permission { get; } = permission;
}
