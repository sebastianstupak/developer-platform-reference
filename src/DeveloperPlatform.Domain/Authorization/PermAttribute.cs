namespace DeveloperPlatform.Domain.Authorization;

[AttributeUsage(AttributeTargets.Field)]
public sealed class PermAttribute(Resource resource, PermissionAction action, string description) : Attribute
{
    public Resource Resource { get; } = resource;
    public PermissionAction Action { get; } = action;
    public string Description { get; } = description;
}
