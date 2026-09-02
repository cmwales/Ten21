using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class LateFeePolicyConfiguration : IEntityTypeConfiguration<LateFeePolicy>
{
    public void Configure(EntityTypeBuilder<LateFeePolicy> builder)
    {
        builder.ToTable("late_fee_policies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.LeaseId).IsRequired();
        builder.Property(p => p.GracePeriodDays).IsRequired();
        builder.Property(p => p.PolicyType).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.BaseAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PercentageRate).HasColumnType("decimal(5,4)");
        builder.Property(p => p.DailyAccrualRate).HasColumnType("decimal(18,2)");
        builder.Property(p => p.MaxFeeCap).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CreatedAt).IsRequired();

        // Zero-or-one policy per lease.
        builder.HasIndex(p => p.LeaseId).IsUnique();

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating.
    }
}
