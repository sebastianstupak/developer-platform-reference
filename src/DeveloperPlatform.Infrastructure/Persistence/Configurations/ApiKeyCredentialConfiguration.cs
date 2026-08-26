using DeveloperPlatform.Domain.ApiKeys;
using DeveloperPlatform.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class ApiKeyCredentialConfiguration : IEntityTypeConfiguration<ApiKeyCredential>
{
    public void Configure(EntityTypeBuilder<ApiKeyCredential> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.KeyPrefix).HasMaxLength(20).IsRequired();
        builder.Property(c => c.KeyHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(c => c.TenantId);
        builder.HasIndex(c => c.ServiceAccountId);
        builder.HasIndex(c => c.KeyHash).IsUnique();   // auth looks keys up by hash

        builder.HasOne<Principal>().WithMany().HasForeignKey(c => c.ServiceAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
