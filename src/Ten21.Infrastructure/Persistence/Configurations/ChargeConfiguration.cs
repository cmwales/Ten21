using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class ChargeConfiguration : IEntityTypeConfiguration<Charge>
{
    public void Configure(EntityTypeBuilder<Charge> builder)
    {
        builder.ToTable("charges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.PropertyId).IsRequired();
        builder.Property(c => c.Description).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Amount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(c => c.DueDate).IsRequired();
        builder.Property(c => c.AccountingCode).HasMaxLength(50);
        builder.Property(c => c.Notes).HasMaxLength(500);
        builder.Property(c => c.Category).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.AllocationPriority).IsRequired();
        builder.Property(c => c.IsStatutoryLocked).IsRequired();
        builder.Property(c => c.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.CreatedAt).IsRequired();

        // No navigation to Property -- ChargesController resolves it by scalar Id lookup,
        // same convention as Lease. Restrict, not Cascade: deleting a Property while a
        // charge still references it should fail loudly, not silently orphan/erase a
        // billing record.
        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(c => c.PropertyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.PropertyId);

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating.
    }
}
