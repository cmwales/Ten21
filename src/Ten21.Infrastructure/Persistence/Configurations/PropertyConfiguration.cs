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
        builder.Property(p => p.StreetAddress).IsRequired().HasMaxLength(255);
        builder.Property(p => p.City).IsRequired().HasMaxLength(100);
        builder.Property(p => p.StateProvince).IsRequired().HasMaxLength(100);
        builder.Property(p => p.PostalCode).IsRequired().HasMaxLength(20);
        builder.Property(p => p.CreatedAt).IsRequired();

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating; EF Core no-ops the duplicate definition, so this
        // comment stands in place of repeating it here.
    }
}
