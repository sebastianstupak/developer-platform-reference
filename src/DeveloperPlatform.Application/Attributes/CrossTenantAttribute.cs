namespace DeveloperPlatform.Application.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CrossTenantAttribute : Attribute
{
    public string Reason { get; }

    public CrossTenantAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("CrossTenant Reason must not be empty.", nameof(reason));
        }

        Reason = reason;
    }
}
