/** Mirrors Ten21.Api.Contracts.ManualCharges.UpsertManualChargeRequest / ManualChargeResponse.
 * US-31: a one-time charge or fine posted to a unit or a specific resident's ledger. */
export interface UpsertManualChargeRequest {
  residentId: string | null;
  description: string;
  amount: number;
  dueDate: string;
  accountingCode: string | null;
}

export interface ManualChargeResponse {
  id: string;
  propertyId: string;
  residentId: string | null;
  description: string;
  amount: number;
  dueDate: string;
  accountingCode: string | null;
}
