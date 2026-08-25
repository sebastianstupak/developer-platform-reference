using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Infrastructure.Context;

public sealed class HttpExecutionContext : IExecutionContext
{
    public Guid TenantId { get; internal set; }
    public Guid? PrincipalId { get; internal set; }
    public PrincipalType? PrincipalType { get; internal set; }
    public Guid? UserId { get; internal set; }
    public Guid? ProjectId { get; internal set; }
    public Guid? EnvironmentId { get; internal set; }
    public string IpAddress { get; internal set; } = string.Empty;
    public bool IsCrossTenantOperation { get; set; }
}
