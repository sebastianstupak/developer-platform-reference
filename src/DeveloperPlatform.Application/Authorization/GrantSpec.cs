using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Authorization;

// A permission to grant at a scope, supplied when creating a service account.
public sealed record GrantSpec(Permission Permission, ScopeType ScopeType, Guid? ScopeTargetId);
