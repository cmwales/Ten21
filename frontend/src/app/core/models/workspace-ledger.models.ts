/** Mirrors Ten21.Api.Contracts.Workspace.PropertyLedgerSummaryResponse. */
export interface PropertyLedgerSummaryResponse {
  propertyId: string;
  propertyName: string;
  unitIdentifier: string | null;
  balance: number;
}

/** Mirrors Ten21.Api.Contracts.Workspace.WorkspaceLedgerResponse -- US-36's portfolio-wide
 * rollup, computed from the same charge/payment/adjustment rows as each property's own
 * unit statement (US-33), not a separately stored total. */
export interface WorkspaceLedgerResponse {
  totalBalance: number;
  properties: PropertyLedgerSummaryResponse[];
}
