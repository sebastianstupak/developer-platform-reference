using DeveloperPlatform.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Email).HasMaxLength(320).IsRequired();
        builder.Property(i => i.Token).HasMaxLength(128).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.ScopeType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Ignore(i => i.Scope);
        builder.HasIndex(i => i.TenantId);
        builder.HasIndex(i => i.Token).IsUnique();

        builder.HasOne<Role>().WithMany().HasForeignKey(i => i.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
