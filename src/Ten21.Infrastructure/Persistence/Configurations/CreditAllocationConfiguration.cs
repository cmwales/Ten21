using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class CreditAllocationConfiguration : IEntityTypeConfiguration<CreditAllocation>
{
    public void Configure(EntityTypeBuilder<CreditAllocation> builder)
    {
        builder.ToTable("credit_allocations");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.SourcePaymentTransactionId).IsRequired();
        builder.Property(a => a.TargetChargeId).IsRequired();
        builder.Property(a => a.AppliedAmount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(a => a.AppliedDate).IsRequired();
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasOne<PaymentTransaction>()
            .WithMany()
            .HasForeignKey(a => a.SourcePaymentTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Charge>()
            .WithMany()
            .HasForeignKey(a => a.TargetChargeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.SourcePaymentTransactionId);
        builder.HasIndex(a => a.TargetChargeId);

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating.
    }
}
