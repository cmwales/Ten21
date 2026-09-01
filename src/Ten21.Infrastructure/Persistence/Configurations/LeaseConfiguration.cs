using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class LeaseConfiguration : IEntityTypeConfiguration<Lease>
{
    public void Configure(EntityTypeBuilder<Lease> builder)
    {
        builder.ToTable("leases");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.PropertyId).IsRequired();
        builder.Property(l => l.ResidentId).IsRequired();
        builder.Property(l => l.StartDate).IsRequired();
        builder.Property(l => l.EndDate).IsRequired();
        builder.Property(l => l.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.CreatedAt).IsRequired();

        // No navigation to Property/ResidentProfile -- LeasesController resolves both by
        // scalar Id lookup, same convention as ResidentProfile's own PropertyId. Restrict,
        // not Cascade: deleting a Property/resident while a lease still references it should
        // fail loudly, not silently orphan/erase a contract record.
        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(l => l.PropertyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ResidentProfile>()
            .WithMany()
            .HasForeignKey(l => l.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(l => l.RecurringCharges)
            .WithOne()
            .HasForeignKey(c => c.LeaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => l.PropertyId);
        builder.HasIndex(l => l.ResidentId);

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating.
    }
}
