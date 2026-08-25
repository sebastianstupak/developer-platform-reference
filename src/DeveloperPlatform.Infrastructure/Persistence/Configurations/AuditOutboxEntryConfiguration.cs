using DeveloperPlatform.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class AuditOutboxEntryConfiguration : IEntityTypeConfiguration<AuditOutboxEntry>
{
    public void Configure(EntityTypeBuilder<AuditOutboxEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => e.ProcessedAt);  // relay worker queries on this
        builder.Property(e => e.CommandType).HasMaxLength(200).IsRequired();
        builder.Property(e => e.IpAddress).HasMaxLength(45).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>();
        builder.Property(e => e.PrincipalType).HasMaxLength(20);
        builder.Property(e => e.EncryptedPayload).IsRequired();
    }
}
