using DeveloperPlatform.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class TenantEncryptionKeyConfiguration : IEntityTypeConfiguration<TenantEncryptionKey>
{
    public void Configure(EntityTypeBuilder<TenantEncryptionKey> builder)
    {
        builder.HasKey(k => k.Id);
        builder.HasIndex(k => k.TenantId);
        builder.Property(k => k.EncryptedKey).IsRequired();
    }
}
