using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class WorkspaceSettingsConfiguration : IEntityTypeConfiguration<WorkspaceSettings>
{
    public void Configure(EntityTypeBuilder<WorkspaceSettings> builder)
    {
        builder.ToTable("workspace_settings");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.TenantId).IsRequired();
        builder.Property(w => w.EnableCommunityDirectory).IsRequired().HasDefaultValue(true);
        builder.Property(w => w.CreatedAt).IsRequired();

        // Exactly one settings row per tenant -- WorkspaceSettingsController's get-or-create
        // relies on this to make a race between two concurrent first-reads fail loudly
        // (unique constraint violation) rather than silently creating two rows.
        builder.HasIndex(w => w.TenantId).IsUnique();
    }
}
