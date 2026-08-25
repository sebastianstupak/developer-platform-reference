using DeveloperPlatform.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class PrincipalConfiguration : IEntityTypeConfiguration<Principal>
{
    public void Configure(EntityTypeBuilder<Principal> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(p => p.TenantId);
    }
}
