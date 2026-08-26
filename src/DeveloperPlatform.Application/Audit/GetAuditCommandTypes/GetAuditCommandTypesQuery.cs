using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Audit.GetAuditCommandTypes;

[RequiresPermission(Permission.AuditRead)]
public record GetAuditCommandTypesQuery : IQuery<IReadOnlyList<string>>;
