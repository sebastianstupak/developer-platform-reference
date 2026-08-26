using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(m => m.TenantId);
        builder.HasIndex(m => m.PrincipalId).IsUnique();
        builder.HasIndex(m => new { m.TenantId, m.UserId }).IsUnique();

        builder.HasOne<Principal>().WithOne().HasForeignKey<Membership>(m => m.PrincipalId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
