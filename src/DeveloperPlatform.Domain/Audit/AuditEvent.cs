using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Audit;

public class AuditEvent : IEntity, ITenantScoped
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public Guid TenantId { get; private set; }
    public DateTime OccurredAt { get; private set; }
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

    private AuditEvent() { }

    public static AuditEvent FromOutboxEntry(AuditOutboxEntry entry) =>
        new()
        {
            TenantId = entry.TenantId,
            OccurredAt = entry.CreatedAt,
            CommandType = entry.CommandType,
            Status = entry.Status,
            UserId = entry.UserId,
            ApiKeyId = entry.ApiKeyId,
            ProjectId = entry.ProjectId,
            EnvironmentId = entry.EnvironmentId,
            IpAddress = entry.IpAddress,
            IsCrossTenant = entry.IsCrossTenant,
            CrossTenantReason = entry.CrossTenantReason,
            EncryptedPayload = entry.EncryptedPayload,
            KeyId = entry.KeyId
        };

    public static AuditEvent Create(
        Guid tenantId, DateTime occurredAt, string commandType, AuditStatus status,
        Guid? userId, Guid? apiKeyId, Guid? projectId, Guid? environmentId,
        string ipAddress, bool isCrossTenant, string? crossTenantReason,
        byte[] encryptedPayload, Guid keyId) =>
        new()
        {
            TenantId = tenantId,
            OccurredAt = occurredAt,
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
