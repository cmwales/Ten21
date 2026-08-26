using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("payment_allocations");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.PaymentTransactionId).IsRequired();
        builder.Property(a => a.ChargeId).IsRequired();
        builder.Property(a => a.AllocatedAmount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(a => a.CreatedAt).IsRequired();

        // Restrict, not Cascade: a Charge should never be hard-deletable while it still has
        // payment history against it (ChargesController doesn't expose hard delete for
        // Charge at all -- soft delete only -- but this is defense in depth).
        builder.HasOne<Charge>()
            .WithMany()
            .HasForeignKey(a => a.ChargeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.PaymentTransactionId);
        builder.HasIndex(a => a.ChargeId);

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating.
    }
}
