namespace DeveloperPlatform.Domain.Authorization;

// A permission/role grant scope: tenant-wide, or pinned to a project or environment.
public readonly record struct Scope
{
    public ScopeType Type { get; }
    public Guid? TargetId { get; }

    private Scope(ScopeType type, Guid? targetId)
    {
        Type = type;
        TargetId = targetId;
    }

    public static Scope Tenant { get; } = new(ScopeType.Tenant, null);
    public static Scope Project(Guid projectId) => Create(ScopeType.Project, projectId);
    public static Scope Environment(Guid environmentId) => Create(ScopeType.Environment, environmentId);

    public static Scope Create(ScopeType type, Guid? targetId)
    {
        if (type == ScopeType.Tenant && targetId is not null)
        {
            throw new ArgumentException("Tenant scope must not have a target id.", nameof(targetId));
        }

        if (type != ScopeType.Tenant && (targetId is null || targetId == Guid.Empty))
        {
            throw new ArgumentException($"{type} scope requires a non-empty target id.", nameof(targetId));
        }

        return new Scope(type, targetId);
    }

    // True when this scope is an ancestor-or-equal of `other` in the scope hierarchy.
    // Tenant ⊇ any Project ⊇ its Environments. Project→Environment nesting is resolved by the
    // authorization service (which knows an environment's parent project); Scope compares by identity.
    public bool Encompasses(Scope other) => this switch
    {
        { Type: ScopeType.Tenant } => true,
        _ => Type == other.Type && TargetId == other.TargetId,
    };
}
