namespace DeveloperPlatform.Web.Http.Models;

public record EnvironmentDto(Guid Id, string Name, string Type, DateTime CreatedAt);

public record SecretDto(string Name, DateTime CreatedAt, DateTime UpdatedAt);

public record RevealDto(string Name, string Value);

public record RotateKeyDto(int SecretsReEncrypted);
