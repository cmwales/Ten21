using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.ToTable("properties");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.PropertyType).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.StreetAddress1).IsRequired().HasMaxLength(255);
        builder.Property(p => p.StreetAddress2).HasMaxLength(255);
        builder.Property(p => p.City).IsRequired().HasMaxLength(100);
        builder.Property(p => p.State).IsRequired().HasMaxLength(100);
        builder.Property(p => p.PostalCode).IsRequired().HasMaxLength(20);
        builder.Property(p => p.Country).IsRequired().HasMaxLength(100);
        builder.Property(p => p.UnitIdentifier).HasMaxLength(50);
        builder.Property(p => p.TargetRent).HasColumnType("decimal(12,2)");
        builder.Property(p => p.OccupancyStatus).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.AllowTenantDirectory).IsRequired();
        builder.Property(p => p.CreatedAt).IsRequired();

        // US-29: Restrict, not Cascade -- deleting a tier/group while properties still
        // reference it should fail loudly (UnitTiersController/UnitGroupsController's own
        // delete actions already guard against this explicitly with a ConflictException
        // before EF Core would ever hit this constraint), not silently null out the
        // assignment. No navigation property, same scalar-FK convention as
        // ResidentProfileConfiguration's Property FK.
        builder.HasOne<UnitGroup>()
            .WithMany()
            .HasForeignKey(p => p.UnitGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnitTier>()
            .WithMany()
            .HasForeignKey(p => p.UnitTierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.UnitGroupId);
        builder.HasIndex(p => p.UnitTierId);

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating; EF Core no-ops the duplicate definition, so this
        // comment stands in place of repeating it here.
    }
}
