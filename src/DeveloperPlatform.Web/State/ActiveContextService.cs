namespace DeveloperPlatform.Web.State;

public readonly record struct ActiveProjectRef(Guid Id, string Name);

public readonly record struct ActiveEnvironmentRef(Guid Id, string Name, string Type);

// Per-circuit holder for the active project/environment. The route is the source of
// truth: pages call SetProject/SetEnvironment on load; the switcher only navigates.
public sealed class ActiveContextService
{
    public ActiveProjectRef? Project { get; private set; }
    public ActiveEnvironmentRef? Environment { get; private set; }

    public event Action? OnChange;

    public void SetProject(ActiveProjectRef? project)
    {
        if (project?.Id != Project?.Id)
        {
            Environment = null;
        }

        Project = project;
        OnChange?.Invoke();
    }

    public void SetEnvironment(ActiveEnvironmentRef? environment)
    {
        Environment = environment;
        OnChange?.Invoke();
    }

    public void Clear()
    {
        Project = null;
        Environment = null;
        OnChange?.Invoke();
    }
}
