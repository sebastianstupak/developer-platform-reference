using Asp.Versioning;
using Asp.Versioning.Builder;

namespace DeveloperPlatform.Api.Endpoints.Health;

public static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapHealth(
        this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        app.MapGet("/health", () => Results.Ok(new HealthResponse("healthy")))
            .WithName("GetHealth")
            .WithTags("Health")
            .WithSummary("Health check")
            .WithDescription("Returns 200 OK when the API is reachable.")
            .Produces<HealthResponse>(StatusCodes.Status200OK)
            .WithApiVersionSet(versionSet)
            .MapToApiVersion(1);

        return app;
    }

    public record HealthResponse(string Status);
}
