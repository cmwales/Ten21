using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class ChargeAdjustmentConfiguration : IEntityTypeConfiguration<ChargeAdjustment>
{
    public void Configure(EntityTypeBuilder<ChargeAdjustment> builder)
    {
        builder.ToTable("charge_adjustments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.TargetChargeId).IsRequired();
        builder.Property(a => a.AdjustmentType).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Amount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(a => a.Reason).IsRequired().HasMaxLength(250);
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasOne<Charge>()
            .WithMany()
            .HasForeignKey(a => a.TargetChargeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.TargetChargeId);

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating.
    }
}
