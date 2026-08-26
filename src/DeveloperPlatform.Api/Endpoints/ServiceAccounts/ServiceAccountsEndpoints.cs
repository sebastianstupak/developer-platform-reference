using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Application.ServiceAccounts.CreateServiceAccount;
using DeveloperPlatform.Application.ServiceAccounts.GetServiceAccounts;
using DeveloperPlatform.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeveloperPlatform.Api.Endpoints.ServiceAccounts;

public static class ServiceAccountsEndpoints
{
    public static IEndpointRouteBuilder MapServiceAccounts(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        app.MapPost("/api/v1/service-accounts", async (
            [FromBody] CreateServiceAccountRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var grants = (request.Grants ?? [])
                .Select(g => new GrantSpec(g.Permission, g.ScopeType, g.ScopeTargetId))
                .ToList();
            var result = await dispatcher.SendAsync<CreateServiceAccountCommand, CreateServiceAccountResult>(
                new CreateServiceAccountCommand(request.Name, request.Description, grants), ct);
            return Results.Created($"/api/v1/service-accounts/{result.ServiceAccountId}",
                new CreateServiceAccountResponse(result.ServiceAccountId));
        })
        .WithName("CreateServiceAccount").WithTags("Service Accounts")
        .WithSummary("Create a service account with permission grants")
        .Produces<CreateServiceAccountResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        app.MapGet("/api/v1/service-accounts", async (IQueryDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(await dispatcher.SendAsync<GetServiceAccountsQuery, IReadOnlyList<ServiceAccountSummary>>(
                new GetServiceAccountsQuery(), ct)))
        .WithName("GetServiceAccounts").WithTags("Service Accounts")
        .WithSummary("List service accounts")
        .Produces<IReadOnlyList<ServiceAccountSummary>>(StatusCodes.Status200OK)
        .WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        return app;
    }

    public record CreateServiceAccountRequest(string Name, string? Description, List<GrantRequest>? Grants);
    public record GrantRequest(Permission Permission, ScopeType ScopeType, Guid? ScopeTargetId);
    public record CreateServiceAccountResponse(Guid ServiceAccountId);
}
