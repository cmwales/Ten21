using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("units");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.TenantId).IsRequired();
        builder.Property(u => u.PropertyId).IsRequired();
        builder.Property(u => u.UnitIdentifier).IsRequired().HasMaxLength(50);
        builder.Property(u => u.TargetRent).HasColumnType("decimal(12,2)");
        builder.Property(u => u.OccupancyStatus).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.CreatedAt).IsRequired();

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating.
        builder.HasIndex(u => u.PropertyId);
    }
}
