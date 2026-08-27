/** Mirrors Ten21.Domain.Enums.ChargeCategory. Drives the US-34 statutory waterfall order:
 * LateFee, Legal, BaseRent, AddOn, SpecialAssessment (highest priority first). */
export const ChargeCategories = {
  LateFee: 'LateFee',
  Legal: 'Legal',
  BaseRent: 'BaseRent',
  AddOn: 'AddOn',
  SpecialAssessment: 'SpecialAssessment',
} as const;

export type ChargeCategoryValue = (typeof ChargeCategories)[keyof typeof ChargeCategories];

/** Mirrors Ten21.Domain.Enums.ChargeLifecycleStatus. */
export const ChargeLifecycleStatuses = {
  Active: 'Active',
  Voided: 'Voided',
} as const;

export type ChargeLifecycleStatusValue = (typeof ChargeLifecycleStatuses)[keyof typeof ChargeLifecycleStatuses];

/** Mirrors Ten21.Domain.Enums.ChargePaymentStatus -- never editable, always computed
 * server-side from payment allocations. */
export const ChargePaymentStatuses = {
  Unpaid: 'Unpaid',
  Partial: 'Partial',
  Paid: 'Paid',
} as const;

export type ChargePaymentStatusValue = (typeof ChargePaymentStatuses)[keyof typeof ChargePaymentStatuses];

/** Mirrors Ten21.Api.Contracts.Charges.UpsertChargeRequest. Renamed/extended from
 * UpsertManualChargeRequest (Sprint 7) -- billed to the unit, never a resident.
 * AllocationPriority is never client-supplied, it's derived server-side from Category. */
export interface UpsertChargeRequest {
  description: string;
  amount: number;
  dueDate: string;
  accountingCode: string | null;
  category: ChargeCategoryValue;
  notes?: string | null;
}

/** Mirrors Ten21.Api.Contracts.Charges.ChargeResponse. AllocatedAmount/OutstandingAmount/
 * PaymentStatus/IsLocked are all computed server-side from payment allocations + adjustments,
 * never stored directly on the charge. */
export interface ChargeResponse {
  id: string;
  propertyId: string;
  description: string;
  amount: number;
  dueDate: string;
  accountingCode: string | null;
  category: ChargeCategoryValue;
  status: ChargeLifecycleStatusValue;
  allocatedAmount: number;
  outstandingAmount: number;
  paymentStatus: ChargePaymentStatusValue;
  isLocked: boolean;
  notes: string | null;
}
