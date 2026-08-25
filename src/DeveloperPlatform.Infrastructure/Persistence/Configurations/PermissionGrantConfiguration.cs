using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class PermissionGrantConfiguration : IEntityTypeConfiguration<PermissionGrant>
{
    public void Configure(EntityTypeBuilder<PermissionGrant> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Permission)
            .HasConversion(new PermissionTokenConverter()).HasMaxLength(100).IsRequired();
        builder.Property(g => g.ScopeType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Ignore(g => g.Scope);
        builder.HasIndex(g => g.TenantId);
        builder.HasIndex(g => g.PrincipalId);

        builder.HasOne<Principal>().WithMany().HasForeignKey(g => g.PrincipalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
