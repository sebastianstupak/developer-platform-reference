using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Authorization;
using DeveloperPlatform.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.HasKey(rp => new { rp.RoleId, rp.Permission });
        builder.Property(rp => rp.Permission)
            .HasConversion(new PermissionTokenConverter()).HasMaxLength(100);

        builder.HasOne<Role>().WithMany().HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasData(SystemRoles.AllPermissions);
    }
}
