using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Audit.GetAuditCommandTypes;

[RequiresPermission(Permission.AuditRead)]
public record GetAuditCommandTypesQuery : IQuery<IReadOnlyList<string>>, IResourceScoped
{
    public Scope ResourceScope => Scope.Tenant;
}
