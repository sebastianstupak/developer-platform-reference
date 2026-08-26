using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace DeveloperPlatform.Infrastructure.Authorization;

// Maps request-shape failures to RFC problem responses: KeyNotFoundException (missing
// project/env/secret) → 404, ArgumentException (bad enum Type string, oversized payload,
// blank required field) → 400.
public sealed class RequestExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var status = exception switch
        {
            KeyNotFoundException => StatusCodes.Status404NotFound,
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => 0
        };
        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc9110",
            title = status == 404 ? "Not Found" : "Bad Request",
            status
        }, ct);
        return true;
    }
}
