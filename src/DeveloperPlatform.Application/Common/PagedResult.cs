namespace DeveloperPlatform.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
