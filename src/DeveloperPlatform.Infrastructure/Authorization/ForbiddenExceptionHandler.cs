using DeveloperPlatform.Application.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace DeveloperPlatform.Infrastructure.Authorization;

// Maps authorization failures to RFC problem responses: ForbiddenException → 403,
// UnauthorizedAccessException (e.g. missing tenant claim) → 401.
public sealed class ForbiddenExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var status = exception switch
        {
            ForbiddenException => StatusCodes.Status403Forbidden,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
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
            title = status == 403 ? "Forbidden" : "Unauthorized",
            status
        }, ct);
        return true;
    }
}
