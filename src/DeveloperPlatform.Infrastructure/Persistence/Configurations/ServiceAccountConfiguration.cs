using DeveloperPlatform.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class ServiceAccountConfiguration : IEntityTypeConfiguration<ServiceAccount>
{
    public void Configure(EntityTypeBuilder<ServiceAccount> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(500);
        builder.HasIndex(s => s.TenantId);
        builder.HasIndex(s => s.PrincipalId).IsUnique();

        builder.HasOne<Principal>().WithOne().HasForeignKey<ServiceAccount>(s => s.PrincipalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
