namespace Ten21.Api.Contracts.Directory;

/// <summary>
/// US-25: deliberately minimal -- name and unit identifier only, never email/phone/
/// emergency contacts (those stay PM-only, via ResidentsController.GetResidents). This is
/// what "directory privacy" means in practice: even a resident who opted in only shares the
/// bare minimum a neighbor would need to recognize who they are.
/// </summary>
public record DirectoryEntryResponse(string FirstName, string LastName, string? UnitIdentifier);

/// <summary>
/// PM-facing verification view of the same dual-consent directory (US-25's
/// GetDirectory), gated by Permissions.Resident.Read instead of Directory.Read -- a PM who
/// can already see full resident PII via ResidentsController.GetResidents loses nothing by
/// seeing it here too, so unlike DirectoryEntryResponse this deliberately includes Email/
/// PhoneNumber. Not scoped to a caller's own occupancy or one property/address group -- a PM
/// needs to see everything currently qualifying across the whole workspace to verify the
/// dual opt-in is working as expected.
/// </summary>
public record DirectoryAdminEntryResponse(
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    string PropertyAddress,
    string? UnitIdentifier);

/// <summary>
/// WorkspaceDirectoryEnabled is surfaced (rather than 403ing like GetDirectory does) so a PM
/// can use this page to verify configuration even while the workspace toggle is off --
/// Entries always reflects the property/resident opt-in state alone; this flag tells the PM
/// whether those entries are actually live for tenants right now.
/// </summary>
public record DirectoryAdminResponse(bool WorkspaceDirectoryEnabled, IReadOnlyList<DirectoryAdminEntryResponse> Entries);
