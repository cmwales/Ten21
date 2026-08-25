using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class ManualChargeConfiguration : IEntityTypeConfiguration<ManualCharge>
{
    public void Configure(EntityTypeBuilder<ManualCharge> builder)
    {
        builder.ToTable("manual_charges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.PropertyId).IsRequired();
        builder.Property(c => c.Description).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Amount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(c => c.DueDate).IsRequired();
        builder.Property(c => c.AccountingCode).HasMaxLength(50);
        builder.Property(c => c.CreatedAt).IsRequired();

        // No navigation to Property/ResidentProfile -- ManualChargesController resolves both
        // by scalar Id lookup, same convention as Lease. Restrict, not Cascade: deleting a
        // Property/resident while a charge still references it should fail loudly, not
        // silently orphan/erase a billing record.
        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(c => c.PropertyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ResidentProfile>()
            .WithMany()
            .HasForeignKey(c => c.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.PropertyId);
        builder.HasIndex(c => c.ResidentId);

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating.
    }
}
