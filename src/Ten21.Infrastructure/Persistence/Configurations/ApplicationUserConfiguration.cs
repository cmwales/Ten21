using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Infrastructure.Identity;

namespace Ten21.Infrastructure.Persistence.Configurations;

/// <summary>
/// Two things beyond ASP.NET Core Identity's defaults:
///   1. Table renamed to snake_case ("users") for consistency with tenants/organizations/
///      properties, instead of Identity's default "AspNetUsers".
///   2. Email uniqueness enforced at the DATABASE level, not just via
///      IdentityOptions.User.RequireUniqueEmail (which only checks at UserManager.CreateAsync
///      time -- a DB-level unique index is the actual guarantee, the same defense-in-depth
///      principle as everywhere else in this codebase).
/// </summary>
public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("users");
        builder.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.LastName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.CreatedAt).IsRequired();

        builder.HasIndex(u => u.NormalizedEmail).IsUnique();
    }
}
