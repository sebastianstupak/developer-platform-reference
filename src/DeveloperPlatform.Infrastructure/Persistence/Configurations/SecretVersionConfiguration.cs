using DeveloperPlatform.Domain.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeveloperPlatform.Infrastructure.Persistence.Configurations;

public class SecretVersionConfiguration : IEntityTypeConfiguration<SecretVersion>
{
    public void Configure(EntityTypeBuilder<SecretVersion> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.EncryptedValue).IsRequired();
        builder.Property(v => v.CreatedByPrincipalType).HasMaxLength(40);
        builder.HasIndex(v => new { v.SecretId, v.VersionNumber }).IsUnique();
        builder.HasOne<Secret>()
            .WithMany()
            .HasForeignKey(v => v.SecretId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
