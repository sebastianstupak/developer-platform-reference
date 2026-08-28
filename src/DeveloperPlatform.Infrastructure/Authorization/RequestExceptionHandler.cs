using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace DeveloperPlatform.Infrastructure.Authorization;

// Maps request-shape failures to RFC problem responses: KeyNotFoundException (missing
// project/env/secret) → 404, ArgumentException (bad enum Type string, oversized payload,
// blank required field) → 400, and a unique-constraint violation (duplicate name) → 409.
public sealed class RequestExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request"),
            DbUpdateException due when IsDuplicateKey(due) => (StatusCodes.Status409Conflict, "Conflict"),
            _ => (0, string.Empty),
        };
        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc9110",
            title,
            status,
        }, ct);
        return true;
    }

    // MySQL / MariaDB duplicate-key error (ER_DUP_ENTRY) — a violated unique index,
    // e.g. two environments or secrets with the same name.
    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is MySqlException { Number: 1062 };
}
