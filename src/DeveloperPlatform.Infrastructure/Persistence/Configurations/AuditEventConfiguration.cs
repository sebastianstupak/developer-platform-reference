using DeveloperPlatform.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => e.OccurredAt);
        builder.Property(e => e.CommandType).HasMaxLength(200).IsRequired();
        builder.Property(e => e.IpAddress).HasMaxLength(45).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>();
        builder.Property(e => e.PrincipalType).HasMaxLength(20);
        builder.Property(e => e.CrossTenantReason).HasMaxLength(500);
        builder.Property(e => e.EncryptedPayload).IsRequired();
    }
}
