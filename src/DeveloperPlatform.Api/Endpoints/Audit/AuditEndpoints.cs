using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Audit.GetAuditEvents;
using DeveloperPlatform.Application.Common;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Audit;

namespace DeveloperPlatform.Api.Endpoints.Audit;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAudit(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v1/audit")
            .WithTags("Audit").WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        group.MapGet("/", async (
            DateTime? from, DateTime? to, Guid? principalId, string? commandType,
            AuditStatus? status, bool? crossTenantOnly, int? page, int? pageSize,
            IQueryDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync<GetAuditEventsQuery, PagedResult<AuditEventSummary>>(
                new GetAuditEventsQuery(
                    new AuditFilter(from, to, principalId, commandType, status, crossTenantOnly),
                    page ?? 1, pageSize ?? 25), ct);

            return Results.Ok(new AuditPageResponse(
                result.Items.Select(i => new AuditEventResponse(
                    i.Id, i.OccurredAt, i.CommandType, i.Status.ToString(), i.ActorDisplay,
                    i.PrincipalType, i.IpAddress, i.IsCrossTenant, i.ProjectId, i.EnvironmentId)).ToList(),
                result.Total, result.Page, result.PageSize));
        }).WithName("GetAuditEvents").Produces<AuditPageResponse>();

        return app;
    }

    public record AuditEventResponse(
        Guid Id, DateTime OccurredAt, string CommandType, string Status, string? ActorDisplay,
        string? PrincipalType, string IpAddress, bool IsCrossTenant, Guid? ProjectId, Guid? EnvironmentId);

    public record AuditPageResponse(
        IReadOnlyList<AuditEventResponse> Items, int Total, int Page, int PageSize);
}
