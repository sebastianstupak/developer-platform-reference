using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Common;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Audit.GetAuditEvents;

[RequiresPermission(Permission.AuditRead)]
public record GetAuditEventsQuery(AuditFilter Filter, int Page, int PageSize)
    : IQuery<PagedResult<AuditEventSummary>>, IResourceScoped
{
    public Scope ResourceScope => Scope.Tenant;
}

// Multi-value filters are OR within a field, AND across fields. An empty list means
// "no constraint" for that field (the pre-multi-select default).
public record AuditFilter(
    DateTime? From, DateTime? To,
    IReadOnlyList<Guid> PrincipalIds, IReadOnlyList<string> CommandTypes,
    IReadOnlyList<AuditStatus> Statuses, bool? CrossTenantOnly);

public record AuditEventSummary(
    Guid Id, DateTime OccurredAt, string CommandType, AuditStatus Status,
    string? ActorDisplay, string? PrincipalType, string IpAddress, bool IsCrossTenant,
    Guid? ProjectId, Guid? EnvironmentId);
