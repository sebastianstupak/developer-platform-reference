using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.Domain.Identity;

// Global identity, keyed by the Keycloak subject. NOT tenant-scoped — a user may belong to
// several tenants (via Membership). JIT-created on first login (Slice 5).
public class User : IEntity
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public string KeycloakSubject { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;

    private User() { }

    public static User Create(string keycloakSubject, string email, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keycloakSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return new User
        {
            KeycloakSubject = keycloakSubject,
            Email = email,
            DisplayName = displayName
        };
    }
}
