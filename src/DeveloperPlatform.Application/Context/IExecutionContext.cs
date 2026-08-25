using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Context;

public interface IExecutionContext
{
    Guid TenantId { get; }
    Guid? PrincipalId { get; }
    PrincipalType? PrincipalType { get; }
    Guid? UserId { get; }          // the human behind a Member principal; null for service accounts
    Guid? ProjectId { get; }
    Guid? EnvironmentId { get; }
    string IpAddress { get; }
    bool IsCrossTenantOperation { get; set; }
}
