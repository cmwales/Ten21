namespace Ten21.Domain.Entities;

/// <summary>
/// A Property Management Company (PMC) operating as the parent container for one or more
/// child <see cref="Tenant"/> records (individual HOAs / property corporations).
///
/// Deliberately NOT ITenantScopedEntity -- an Organization sits *above* the tenant boundary,
/// not inside it. Self-managed HOAs never get an Organization row at all
/// (Tenant.OrganizationId stays null).
/// </summary>
public class Organization
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string SubscriptionTier { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
