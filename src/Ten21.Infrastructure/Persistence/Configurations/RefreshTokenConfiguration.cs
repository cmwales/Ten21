using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;
using Ten21.Infrastructure.Identity;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.TokenHash).IsRequired().HasMaxLength(128);
        // Lookups always happen by hash (the raw value is never persisted, never queried)
        // -- this is the index every refresh/revoke request actually hits.
        builder.HasIndex(rt => rt.TokenHash).IsUnique();

        builder.Property(rt => rt.CreatedByIp).HasMaxLength(64);
        builder.Property(rt => rt.RevokedByIp).HasMaxLength(64);
        builder.Property(rt => rt.ExpiresAt).IsRequired();
        builder.Property(rt => rt.CreatedAt).IsRequired();

        // Deliberately no HasIndex(rt => rt.TenantId) and no ITenantScopedEntity on this
        // entity at all -- see the class-level comment on RefreshToken for why.
        builder.HasIndex(rt => rt.UserId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
