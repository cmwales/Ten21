using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class UnitGroupConfiguration : IEntityTypeConfiguration<UnitGroup>
{
    public void Configure(EntityTypeBuilder<UnitGroup> builder)
    {
        builder.ToTable("unit_groups");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.TenantId).IsRequired();
        builder.Property(g => g.GroupName).IsRequired().HasMaxLength(100);
        builder.Property(g => g.Description).HasMaxLength(500);
        builder.Property(g => g.CreatedAt).IsRequired();

        // TenantId index is also added generically for every ITenantScopedEntity in
        // Ten21DbContext.OnModelCreating; EF Core no-ops the duplicate definition.
    }
}
