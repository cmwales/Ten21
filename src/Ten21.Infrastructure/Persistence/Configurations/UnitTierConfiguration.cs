using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class UnitTierConfiguration : IEntityTypeConfiguration<UnitTier>
{
    public void Configure(EntityTypeBuilder<UnitTier> builder)
    {
        builder.ToTable("unit_tiers");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.TierName).IsRequired().HasMaxLength(100);
        builder.Property(t => t.DefaultRent).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(t => t.AccountingCode).HasMaxLength(50);
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.CreatedAt).IsRequired();

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating; EF Core no-ops the duplicate definition.
    }
}
