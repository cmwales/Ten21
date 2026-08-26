/** Mirrors Ten21.Api.Contracts.ManualCharges.UpsertManualChargeRequest / ManualChargeResponse.
 * US-31: a one-time charge or fine posted to a unit's ledger. Post-Sprint-6 fix: no more
 * per-resident "bill to" -- charges are billed to the unit, not an individual occupant.
 * PaidDate records when payment was actually received (may differ from when it was entered
 * into the system). */
export interface UpsertManualChargeRequest {
  description: string;
  amount: number;
  dueDate: string;
  accountingCode: string | null;
  paidDate: string | null;
}

export interface ManualChargeResponse {
  id: string;
  propertyId: string;
  description: string;
  amount: number;
  dueDate: string;
  accountingCode: string | null;
  paidDate: string | null;
}
