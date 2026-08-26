namespace DeveloperPlatform.Web.Http.Models;

public record AuditEventDto(
    Guid Id,
    DateTime OccurredAt,
    string CommandType,
    string Status,
    string? ActorDisplay,
    string? PrincipalType,
    string IpAddress,
    bool IsCrossTenant,
    Guid? ProjectId,
    Guid? EnvironmentId);

public record AuditPageDto(IReadOnlyList<AuditEventDto> Items, int Total, int Page, int PageSize);

public record AuditDetailDto(AuditEventDto Event, string? CrossTenantReason, string PayloadJson, bool PayloadAvailable);

public record AuditFilterDto(
    DateTime? From,
    DateTime? To,
    Guid? PrincipalId,
    string? CommandType,
    string? Status,
    bool? CrossTenantOnly);
