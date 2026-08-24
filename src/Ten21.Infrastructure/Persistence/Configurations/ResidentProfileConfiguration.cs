using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class ResidentProfileConfiguration : IEntityTypeConfiguration<ResidentProfile>
{
    public void Configure(EntityTypeBuilder<ResidentProfile> builder)
    {
        builder.ToTable("resident_profiles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.PropertyId).IsRequired();
        builder.Property(r => r.OccupantType).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(r => r.LastName).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Email).HasMaxLength(256);
        builder.Property(r => r.PhoneNumber).HasMaxLength(30);
        builder.Property(r => r.ForwardingAddress).HasMaxLength(255);
        builder.Property(r => r.ShowInDirectory).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();

        // No navigation to Property -- PropertiesController/ResidentsController resolve it
        // by scalar PropertyId lookup, same convention as TenantMembership's scalar
        // UserId/RoleId (avoids a Domain -> Infrastructure-shaped navigation graph growing
        // unchecked). Restrict, not Cascade: deleting a Property while it still has resident
        // rows should fail loudly, not silently orphan/erase occupant history.
        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(r => r.PropertyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.EmergencyContacts)
            .WithOne()
            .HasForeignKey(c => c.ResidentProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.PropertyId);

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating; EF Core no-ops the duplicate definition, so this
        // comment stands in place of repeating it here.
    }
}
