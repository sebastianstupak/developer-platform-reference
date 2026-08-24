namespace DeveloperPlatform.Application.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CrossTenantAttribute : Attribute
{
    public string Reason { get; init; } = string.Empty;

    public CrossTenantAttribute() { }

    public CrossTenantAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("CrossTenant Reason must not be empty.", nameof(reason));
        Reason = reason;
    }
}
