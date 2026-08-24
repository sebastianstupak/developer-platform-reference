namespace DeveloperPlatform.Infrastructure.Messaging;

public sealed record AuditMessage(
    Guid Id,
    Guid TenantId,
    string CommandType,
    string Status,
    Guid? UserId,
    Guid? ApiKeyId,
    Guid? ProjectId,
    Guid? EnvironmentId,
    string IpAddress,
    bool IsCrossTenant,
    string? CrossTenantReason,
    byte[] EncryptedPayload,
    Guid KeyId,
    DateTime OccurredAt);
