using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class SecurityDepositConfiguration : IEntityTypeConfiguration<SecurityDeposit>
{
    public void Configure(EntityTypeBuilder<SecurityDeposit> builder)
    {
        builder.ToTable("security_deposits");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.TenantId).IsRequired();
        builder.Property(d => d.PropertyId).IsRequired();
        builder.Property(d => d.ResidentProfileId).IsRequired();
        builder.Property(d => d.OriginalAmount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(d => d.AmountHeld).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(d => d.CollectedDate).IsRequired();
        builder.Property(d => d.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.CreatedAt).IsRequired();

        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(d => d.PropertyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ResidentProfile>()
            .WithMany()
            .HasForeignKey(d => d.ResidentProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.PropertyId);
        builder.HasIndex(d => d.ResidentProfileId);

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating.
    }
}
