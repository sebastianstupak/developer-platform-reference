using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Common;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Audit.GetAuditEvents;

[RequiresPermission(Permission.AuditRead)]
public record GetAuditEventsQuery(AuditFilter Filter, int Page, int PageSize)
    : IQuery<PagedResult<AuditEventSummary>>;

public record AuditFilter(
    DateTime? From, DateTime? To, Guid? PrincipalId, string? CommandType,
    AuditStatus? Status, bool? CrossTenantOnly);

public record AuditEventSummary(
    Guid Id, DateTime OccurredAt, string CommandType, AuditStatus Status,
    string? ActorDisplay, string? PrincipalType, string IpAddress, bool IsCrossTenant,
    Guid? ProjectId, Guid? EnvironmentId);
