using Ten21.Domain.Common;

namespace Ten21.Domain.Entities;

/// <summary>
/// Refinement Sprint (Directive 4): one row per tenant/workspace holding admin-configurable,
/// workspace-wide feature toggles -- starting with EnableCommunityDirectory. Deliberately a
/// single mutable settings row per TenantId rather than a generic key/value flag table: there's
/// exactly one toggle so far, and FEATURES.md's "grow feature-by-feature" convention (see
/// Permissions' own comment) applies here too -- add columns as concrete settings are actually
/// built, not a speculative flags schema upfront.
///
/// Lazily created on first read (WorkspaceSettingsController.GetOrCreate) rather than seeded at
/// tenant-provisioning time, so this never blocks the zero-touch self-service provisioning flow
/// on a new column being added here later.
/// </summary>
public class WorkspaceSettings : ITenantScopedEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public bool EnableCommunityDirectory { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}
