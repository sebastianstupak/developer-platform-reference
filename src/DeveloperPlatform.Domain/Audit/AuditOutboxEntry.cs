using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Audit;

public enum AuditStatus { Success, Failed }

public class AuditOutboxEntry : IEntity, ITenantScoped
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public Guid TenantId { get; private set; }
    public string CommandType { get; private set; } = string.Empty;
    public AuditStatus Status { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid? ApiKeyId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public Guid? EnvironmentId { get; private set; }
    public string IpAddress { get; private set; } = string.Empty;
    public bool IsCrossTenant { get; private set; }
    public string? CrossTenantReason { get; private set; }
    public byte[] EncryptedPayload { get; private set; } = [];
    public Guid KeyId { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public DateTime? FailedAt { get; private set; }
    public int RetryCount { get; private set; }

    private AuditOutboxEntry() { }

    public static AuditOutboxEntry Create(
        Guid tenantId, string commandType, AuditStatus status,
        Guid? userId, Guid? apiKeyId, Guid? projectId, Guid? environmentId,
        string ipAddress, bool isCrossTenant, string? crossTenantReason,
        byte[] encryptedPayload, Guid keyId)
    {
        return new AuditOutboxEntry
        {
            TenantId = tenantId,
            CommandType = commandType,
            Status = status,
            UserId = userId,
            ApiKeyId = apiKeyId,
            ProjectId = projectId,
            EnvironmentId = environmentId,
            IpAddress = ipAddress,
            IsCrossTenant = isCrossTenant,
            CrossTenantReason = crossTenantReason,
            EncryptedPayload = encryptedPayload,
            KeyId = keyId
        };
    }

    public void MarkProcessed() => ProcessedAt = DateTime.UtcNow;

    public void MarkFailed()
    {
        FailedAt = DateTime.UtcNow;
        RetryCount++;
    }
}
