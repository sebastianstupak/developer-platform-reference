namespace DeveloperPlatform.Application.Context;

public interface IExecutionContext
{
    Guid TenantId { get; }
    Guid? UserId { get; }
    Guid? ApiKeyId { get; }
    Guid? ProjectId { get; }
    Guid? EnvironmentId { get; }
    string IpAddress { get; }
    bool IsCrossTenantOperation { get; set; }
}
