using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.EntityName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.EntityId).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(20);
        builder.Property(a => a.ChangedAtUtc).IsRequired();

        builder.HasIndex(a => new { a.EntityName, a.EntityId });
    }
}
