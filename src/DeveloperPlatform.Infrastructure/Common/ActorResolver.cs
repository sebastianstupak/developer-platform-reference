namespace DeveloperPlatform.Infrastructure.Common;

// Shared "who did this" resolution: a Member resolves to the user's email,
// a ServiceAccount to its name, otherwise the raw principal id.
public static class ActorResolver
{
    public static string? Resolve(
        string? principalType, Guid? userId, Guid? principalId,
        IReadOnlyDictionary<Guid, string> users, IReadOnlyDictionary<Guid, string> serviceAccounts)
    {
        if (principalType == "Member" && userId is { } uid && users.TryGetValue(uid, out var email))
        {
            return email;
        }

        if (principalType == "ServiceAccount" && principalId is { } pid && serviceAccounts.TryGetValue(pid, out var name))
        {
            return name;
        }

        return principalId?.ToString();
    }
}
