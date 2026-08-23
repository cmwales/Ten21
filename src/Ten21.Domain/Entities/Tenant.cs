namespace Ten21.Domain.Entities;

/// <summary>
/// The tenant registry row itself -- one row per isolated HOA / property corporation /
/// self-storage facility. This is the entity whose Id every ITenantScopedEntity.TenantId
/// points at.
///
/// Deliberately NOT ITenantScopedEntity: a tenant cannot belong to itself. This table is
/// the one exception to the global query filter and is only ever queried by platform-level
/// concerns (onboarding, org context-switch validation, SuperAdmin tooling).
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>
    /// Optional parent PMC. Null for self-managed HOAs / independent landlords.
    /// </summary>
    public Guid? OrganizationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
