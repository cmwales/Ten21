using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class BillingCycleRunConfiguration : IEntityTypeConfiguration<BillingCycleRun>
{
    public void Configure(EntityTypeBuilder<BillingCycleRun> builder)
    {
        builder.ToTable("billing_cycle_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.RunDate).IsRequired();
        builder.Property(r => r.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.ErrorMessage).HasMaxLength(2000);
        builder.Property(r => r.TriggeredBy).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.StartedAt).IsRequired();

        builder.HasIndex(r => new { r.TenantId, r.RunDate });
        builder.HasIndex(r => r.Status);
    }
}
