using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;
using Ten21.Infrastructure.Identity;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("tenant_memberships");
        builder.HasKey(tm => tm.Id);
        builder.Property(tm => tm.CreatedAt).IsRequired();

        // Same person can never hold the exact same role twice within the same tenant --
        // catches accidental double-invites at the DB level, not just in application code.
        builder.HasIndex(tm => new { tm.UserId, tm.TenantId, tm.RoleId }).IsUnique();

        // Relationships wired here via HasForeignKey against the scalar Guid properties --
        // TenantMembership (Domain) has no navigation property to either Identity type,
        // by design (see the class-level comment on TenantMembership for why).
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(tm => tm.UserId)
            .OnDelete(DeleteBehavior.Cascade); // delete the user, their memberships go with them

        builder.HasOne<ApplicationRole>()
            .WithMany()
            .HasForeignKey(tm => tm.RoleId)
            .OnDelete(DeleteBehavior.Restrict); // a role in active use can't be deleted out from under memberships
    }
}
