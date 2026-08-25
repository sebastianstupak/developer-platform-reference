namespace DeveloperPlatform.Web.Http.Models;

public record ProjectDto(Guid Id, string Name, string? Description, DateTime CreatedAt);
