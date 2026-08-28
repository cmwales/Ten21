/** Mirrors Ten21.Api.Contracts.Directory.DirectoryEntryResponse (US-25). Deliberately just a
 * name + unit -- the dual-consent community directory never exposes contact info. */
export interface DirectoryEntryResponse {
  firstName: string;
  lastName: string;
  unitIdentifier: string | null;
}
