using DeveloperPlatform.Application.Context;

namespace DeveloperPlatform.Infrastructure.Context;

public sealed class HttpExecutionContext : IExecutionContext
{
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? ApiKeyId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? EnvironmentId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public bool IsCrossTenantOperation { get; set; }
}
