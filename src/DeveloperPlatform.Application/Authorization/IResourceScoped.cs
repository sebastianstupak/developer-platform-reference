using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Authorization;

// A command/query implements this to declare the specific resource scope it acts on,
// so per-instance ACLs (e.g. "write on project X") are enforced.
public interface IResourceScoped
{
    Scope ResourceScope { get; }
}
