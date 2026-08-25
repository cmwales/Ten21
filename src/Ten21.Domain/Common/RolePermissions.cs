namespace Ten21.Domain.Common;

/// <summary>
/// Maps each of the 9 system roles (RoleNames) to its additive permission-claim bundle
/// (Permissions). Deliberately a STARTING POINT, not a final claims matrix -- FEATURES.docx
/// §1 requires every new feature user story to explicitly declare Primary/Secondary/
/// Prohibited roles and Required Permission Claims per endpoint, so these bundles are
/// expected to grow feature-by-feature through Phase 2, not be front-loaded speculatively.
/// Every grant below is traceable to specific wording in SECURITY.docx §4.1 or
/// BUSINESS_RULES.docx §1 -- see the inline comment on each.
/// </summary>
public static class RolePermissions
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Bundles =
        new Dictionary<string, IReadOnlyList<string>>
        {
            // "Internal platform administration... across all HOAs/PMCs" -- reflected from
            // Permissions.All rather than hand-listed, so this never silently falls behind
            // when a new permission category is added elsewhere.
            [RoleNames.SuperAdmin] = Permissions.All,

            // "Fiduciary oversight, financial ledger access, executive notes, and official
            // community voting setup."
            [RoleNames.BoardMember] =
            [
                Permissions.Ledger.Read, Permissions.Ledger.Write,
                Permissions.Voting.Cast, Permissions.Voting.ManageBallots,
                Permissions.WorkOrders.Read,
                Permissions.Announcements.Read, Permissions.Announcements.Write,
            ],

            // "Personal dues ledgers, ARC submissions, tax forms, and official owner
            // election ballots." Property.Read added in Sprint 6 (US-29) -- that story
            // names Property Owner as an authorized secondary role ("Permissions.Property.Read")
            // for both the property list and the unit tier/group matrix, a read-only grant
            // this bundle didn't have yet.
            [RoleNames.PropertyOwner] =
            [
                Permissions.Ledger.Read, Permissions.Arc.Submit, Permissions.Voting.Cast,
                Permissions.WorkOrders.Read, Permissions.Announcements.Read,
                Permissions.Property.Read,
            ],

            // "Day-to-day operations, vendor routing, work orders, and community admin.
            // Cannot cast HOA board votes" -- deliberately no Voting.* grant. Ledger.Read
            // only (operational visibility) -- BUSINESS_RULES §1 reserves ledger statements
            // and financial write authority to Property Owner / Board / Accountant, not PM.
            // Sprint 3 (US-19-22): "Primary Role: Property Manager" on every property-setup
            // story, and no other role is named as an authorized secondary -- least
            // privilege, so only PropertyManager (and SuperAdmin, via Permissions.All) gets
            // these until a future story explicitly says otherwise.
            // Sprint 4 (US-23): "Primary Role: Property Manager" on the resident-directory
            // story, no other role named as an authorized secondary -- same
            // least-privilege reasoning as Sprint 3's Property.* grants above.
            [RoleNames.PropertyManager] =
            [
                Permissions.WorkOrders.Read, Permissions.WorkOrders.Write,
                Permissions.Announcements.Read, Permissions.Announcements.Write,
                Permissions.Ledger.Read,
                Permissions.Property.Manage, Permissions.Property.Read,
                Permissions.Property.Import, Permissions.Property.Delete,
                Permissions.Resident.Manage, Permissions.Resident.Read,
            ],

            // "Restricted strictly to maintenance tickets, amenity booking, and community
            // announcements. Zero access to financial ledgers or voting." No Ledger.*, no
            // Voting.*, no ARC.* (ARC alteration requests are owner-only per BUSINESS_RULES §1).
            // Directory.Read added in Sprint 4 (US-25) -- the community directory is a
            // Tenant-facing feature by definition (residents viewing opted-in neighbors),
            // no other role needs it: a PM already sees every resident of their own
            // properties unfiltered via Permissions.Resident.Read, dual-consent privacy
            // doesn't apply to their own management view.
            [RoleNames.Tenant] =
            [
                Permissions.WorkOrders.Write, Permissions.Announcements.Read, Permissions.Directory.Read,
            ],

            // "External contractor handling assigned work order status."
            [RoleNames.Vendor] =
            [
                Permissions.WorkOrders.Read, Permissions.WorkOrders.Write,
            ],

            // "Scoped review and voting rights over specific operational workflows without
            // financial ledger access" -- ARC/Social committee is the concrete example given.
            [RoleNames.CommitteeMember] =
            [
                Permissions.Arc.Approve,
            ],

            // "Package logging, amenity check-ins, and work order execution." Package/amenity
            // permission categories don't exist yet in this Phase-0 catalog -- add them here
            // when those features (Phase 2) are actually built, not speculatively now.
            [RoleNames.OnSiteStaff] =
            [
                Permissions.WorkOrders.Read, Permissions.WorkOrders.Write,
            ],

            // "Read/write ledger access and bank reconciliation rights without
            // administrative or lease management access."
            [RoleNames.Accountant] =
            [
                Permissions.Ledger.Read, Permissions.Ledger.Write,
            ],
        };
}
