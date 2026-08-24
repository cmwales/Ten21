using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class EmergencyContactConfiguration : IEntityTypeConfiguration<EmergencyContact>
{
    public void Configure(EntityTypeBuilder<EmergencyContact> builder)
    {
        builder.ToTable("emergency_contacts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.ResidentProfileId).IsRequired();
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.PhoneNumber).IsRequired().HasMaxLength(30);
        builder.Property(c => c.Relationship).HasMaxLength(50);
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasIndex(c => c.ResidentProfileId);

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating.
    }
}
