using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class LeaseRecurringChargeConfiguration : IEntityTypeConfiguration<LeaseRecurringCharge>
{
    public void Configure(EntityTypeBuilder<LeaseRecurringCharge> builder)
    {
        builder.ToTable("lease_recurring_charges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.LeaseId).IsRequired();
        builder.Property(c => c.ChargeName).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Category).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Amount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(c => c.AccountingCode).HasMaxLength(50);
        builder.Property(c => c.Description).HasMaxLength(200);
        builder.Property(c => c.RecurrencePattern).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.RecurrenceInterval).IsRequired();
        builder.Property(c => c.TargetDayOfWeek).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.EndStrategy).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.EffectiveStartDate).IsRequired();
        builder.Property(c => c.ProrationStrategy).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.IsPaused).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasIndex(c => c.LeaseId);

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating.
    }
}
