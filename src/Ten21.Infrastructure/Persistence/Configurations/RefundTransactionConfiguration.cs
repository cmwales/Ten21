using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class RefundTransactionConfiguration : IEntityTypeConfiguration<RefundTransaction>
{
    public void Configure(EntityTypeBuilder<RefundTransaction> builder)
    {
        builder.ToTable("refund_transactions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.ResidentProfileId).IsRequired();
        builder.Property(r => r.PropertyId).IsRequired();
        builder.Property(r => r.Amount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(r => r.RefundDate).IsRequired();
        builder.Property(r => r.TenderType).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.ReferenceNumber).HasMaxLength(100);
        builder.Property(r => r.Reason).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.CreatedAt).IsRequired();

        builder.HasOne<ResidentProfile>()
            .WithMany()
            .HasForeignKey(r => r.ResidentProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(r => r.PropertyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.ResidentProfileId);
        builder.HasIndex(r => r.PropertyId);

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating.
    }
}
