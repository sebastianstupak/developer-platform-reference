using DeveloperPlatform.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ScopeType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Ignore(a => a.Scope);
        builder.HasIndex(a => a.TenantId);
        builder.HasIndex(a => a.PrincipalId);

        builder.HasOne<Principal>().WithMany().HasForeignKey(a => a.PrincipalId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Role>().WithMany().HasForeignKey(a => a.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
