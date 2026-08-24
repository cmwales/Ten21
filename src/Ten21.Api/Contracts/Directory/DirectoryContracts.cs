namespace Ten21.Api.Contracts.Directory;

/// <summary>
/// US-25: deliberately minimal -- name and unit identifier only, never email/phone/
/// emergency contacts (those stay PM-only, via ResidentsController.GetResidents). This is
/// what "directory privacy" means in practice: even a resident who opted in only shares the
/// bare minimum a neighbor would need to recognize who they are.
/// </summary>
public record DirectoryEntryResponse(string FirstName, string LastName, string? UnitIdentifier);
