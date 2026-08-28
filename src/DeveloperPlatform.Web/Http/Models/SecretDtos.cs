namespace DeveloperPlatform.Web.Http.Models;

public record EnvironmentDto(
    Guid Id, string Name, string Type, DateTime CreatedAt,
    int SecretCount, DateTime LastUpdatedAt);

public record SecretDto(string Name, DateTime CreatedAt, DateTime UpdatedAt);

public record RevealDto(string Name, string Value);

public record RotateKeyDto(int SecretsReEncrypted);

public record SecretVersionDto(int VersionNumber, DateTime CreatedAt, string? Actor, bool IsCurrent, int? RolledBackFrom);

public record RevealVersionDto(string Name, int VersionNumber, string Value);
