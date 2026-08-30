/** Mirrors Ten21.Api.Contracts.Directory.DirectoryEntryResponse (US-25). Deliberately just a
 * name + unit -- the dual-consent community directory never exposes contact info. */
export interface DirectoryEntryResponse {
  firstName: string;
  lastName: string;
  unitIdentifier: string | null;
}

/** Mirrors Ten21.Api.Contracts.Directory.DirectoryAdminEntryResponse -- the PM-facing
 * verification view, which (unlike DirectoryEntryResponse) includes contact info since a PM
 * with Permissions.Resident.Read already sees this same PII elsewhere. */
export interface DirectoryAdminEntryResponse {
  firstName: string;
  lastName: string;
  email: string | null;
  phoneNumber: string | null;
  propertyAddress: string;
  unitIdentifier: string | null;
}

/** Mirrors Ten21.Api.Contracts.Directory.DirectoryAdminResponse. */
export interface DirectoryAdminResponse {
  workspaceDirectoryEnabled: boolean;
  entries: DirectoryAdminEntryResponse[];
}
