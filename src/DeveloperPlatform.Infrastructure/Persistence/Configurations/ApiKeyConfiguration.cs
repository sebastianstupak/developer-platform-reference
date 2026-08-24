using DeveloperPlatform.Domain.ApiKeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Name).HasMaxLength(200).IsRequired();
        builder.Property(k => k.KeyPrefix).HasMaxLength(20).IsRequired();
        builder.Property(k => k.KeyHash).HasMaxLength(256).IsRequired();
        builder.Property(k => k.Scopes).HasConversion<int>();
        builder.HasIndex(k => k.TenantId);
        builder.HasIndex(k => k.ProjectId);
    }
}
