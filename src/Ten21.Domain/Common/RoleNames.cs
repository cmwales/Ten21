namespace Ten21.Domain.Common;

/// <summary>
/// The 9-tier system role taxonomy (SECURITY.docx §4.1), as plain string constants.
///
/// Deliberately just strings, not an enum: ASP.NET Core Identity's ApplicationRole.Name is
/// a string column (roles are DB rows, not a closed enum, since US-03's domain-neutral
/// role filtering later needs to activate/deactivate roles per property type without a
/// code change). This class exists purely so "PropertyManager" isn't typed as a magic
/// string in five different places -- role seeding here, claims policies in US-03, and
/// tests all reference these constants instead.
/// </summary>
public static class RoleNames
{
    public const string SuperAdmin = "SuperAdmin";
    public const string PropertyManager = "PropertyManager";
    public const string BoardMember = "BoardMember";
    public const string PropertyOwner = "PropertyOwner";
    public const string Tenant = "Tenant";
    public const string Vendor = "Vendor";
    public const string CommitteeMember = "CommitteeMember";
    public const string OnSiteStaff = "OnSiteStaff";
    public const string Accountant = "Accountant";

    public static readonly IReadOnlyList<string> All =
    [
        SuperAdmin, PropertyManager, BoardMember, PropertyOwner,
        Tenant, Vendor, CommitteeMember, OnSiteStaff, Accountant
    ];
}
