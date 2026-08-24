using System.Reflection;

namespace Ten21.Domain.Common;

/// <summary>
/// The additive permission-claim vocabulary (SECURITY.docx §4.2, e.g. "Permissions.Ledger.Read",
/// "Permissions.WorkOrders.Write"). Deliberately a SMALL, doc-grounded starting set, not a
/// front-loaded speculative catalog: FEATURES.docx §1 requires every new feature user story
/// to declare its own Required Permission Claims explicitly, so this grows feature-by-feature
/// through Phase 2, not all at once now for features that don't exist yet.
/// </summary>
public static class Permissions
{
    public static class Ledger
    {
        public const string Read = "Permissions.Ledger.Read";
        public const string Write = "Permissions.Ledger.Write";
    }

    public static class WorkOrders
    {
        public const string Read = "Permissions.WorkOrders.Read";
        public const string Write = "Permissions.WorkOrders.Write";
    }

    public static class Arc
    {
        public const string Submit = "Permissions.ARC.Submit";
        public const string Approve = "Permissions.ARC.Approve";
    }

    public static class Voting
    {
        public const string Cast = "Permissions.Voting.Cast";
        public const string ManageBallots = "Permissions.Voting.ManageBallots";
    }

    public static class Announcements
    {
        public const string Read = "Permissions.Announcements.Read";
        public const string Write = "Permissions.Announcements.Write";
    }

    public static class Property
    {
        public const string Manage = "Permissions.Property.Manage";
        public const string Read = "Permissions.Property.Read";
        public const string Import = "Permissions.Property.Import";
        public const string Delete = "Permissions.Property.Delete";
    }

    public static class Resident
    {
        public const string Manage = "Permissions.Resident.Manage";
        public const string Read = "Permissions.Resident.Read";
    }

    public static class Directory
    {
        public const string Read = "Permissions.Directory.Read";
    }

    /// <summary>
    /// Every permission constant above, discovered via reflection rather than hand-maintained.
    /// This is what lets policy registration (Infrastructure.AuthorizationConfiguration) and
    /// SuperAdmin's "every permission" bundle (RolePermissions) stay correct automatically as
    /// new permission categories are added later, instead of needing a second list kept in
    /// sync by hand -- the same reflection-over-registration principle as Ten21DbContext's
    /// tenant query filters in US-01.
    /// </summary>
    public static readonly IReadOnlyList<string> All = DiscoverAll();

    private static IReadOnlyList<string> DiscoverAll()
    {
        var values = new List<string>();
        foreach (var nestedType in typeof(Permissions).GetNestedTypes())
        {
            foreach (var field in nestedType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (field.IsLiteral && field.FieldType == typeof(string))
                {
                    values.Add((string)field.GetRawConstantValue()!);
                }
            }
        }
        return values;
    }
}
