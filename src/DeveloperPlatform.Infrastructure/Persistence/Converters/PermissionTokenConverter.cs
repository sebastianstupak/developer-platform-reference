using DeveloperPlatform.Domain.Authorization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DeveloperPlatform.Infrastructure.Persistence.Converters;

// Persists a Permission as its canonical resource:action token (single source of truth = PermissionCatalog).
public sealed class PermissionTokenConverter : ValueConverter<Permission, string>
{
    public PermissionTokenConverter()
        : base(p => PermissionCatalog.ToToken(p), s => PermissionCatalog.FromToken(s))
    {
    }
}
