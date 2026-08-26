using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Audit.GetAuditEvents;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Audit.GetAuditEventDetail;

[RequiresPermission(Permission.AuditRead)]
public record GetAuditEventDetailQuery(Guid Id) : IQuery<AuditEventDetail>, IResourceScoped
{
    public Scope ResourceScope => Scope.Tenant;
}

public record AuditEventDetail(
    AuditEventSummary Summary, string? CrossTenantReason, string PayloadJson, bool PayloadAvailable);
