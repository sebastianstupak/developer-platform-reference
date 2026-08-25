namespace DeveloperPlatform.Application.Authorization;

// Thrown when a principal lacks a required permission. Mapped to HTTP 403 by the API.
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}
