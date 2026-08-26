import { ChargeResponse } from './charge.models';

/** Mirrors Ten21.Domain.Enums.TenderType. */
export const TenderTypes = {
  Cash: 'Cash',
  Check: 'Check',
  Zelle: 'Zelle',
  Venmo: 'Venmo',
  DirectDeposit: 'DirectDeposit',
} as const;

export type TenderTypeValue = (typeof TenderTypes)[keyof typeof TenderTypes];

/** Mirrors Ten21.Domain.Enums.AdjustmentType. */
export const AdjustmentTypes = {
  CreditAdjustment: 'CreditAdjustment',
  DebitAdjustment: 'DebitAdjustment',
} as const;

export type AdjustmentTypeValue = (typeof AdjustmentTypes)[keyof typeof AdjustmentTypes];

/** Mirrors Ten21.Api.Contracts.Charges.ChargeAdjustmentResponse. */
export interface ChargeAdjustmentResponse {
  id: string;
  adjustmentType: AdjustmentTypeValue;
  amount: number;
  reason: string;
  createdAt: string;
}

/** Mirrors Ten21.Api.Contracts.Charges.ChargeStatementItemResponse -- a charge with its
 * adjustments nested directly beneath it, per the statement's "indented" display rule. */
export interface ChargeStatementItemResponse {
  charge: ChargeResponse;
  adjustments: ChargeAdjustmentResponse[];
}

/** Mirrors Ten21.Api.Contracts.Charges.PaymentAllocationSummaryResponse. */
export interface PaymentAllocationSummaryResponse {
  chargeId: string;
  chargeDescription: string;
  allocatedAmount: number;
}

/** Mirrors Ten21.Api.Contracts.Charges.PaymentTransactionResponse. ResidentProfileId is
 * required -- money belongs to a specific payee (for refund routing, co-tenant attribution,
 * and per-resident history), even though Charges stay unit-scoped. */
export interface PaymentTransactionResponse {
  id: string;
  propertyId: string;
  residentProfileId: string;
  residentName: string;
  paymentDate: string;
  amountPaid: number;
  tenderType: TenderTypeValue;
  referenceNumber: string | null;
  notes: string | null;
  allocations: PaymentAllocationSummaryResponse[];
}

/** Mirrors Ten21.Api.Contracts.Charges.UnitStatementResponse -- the whole "lifetime
 * financial statement" for one unit: charges (with nested adjustments), payments, and the
 * dynamic running Balance. */
export interface UnitStatementResponse {
  propertyId: string;
  balance: number;
  charges: ChargeStatementItemResponse[];
  payments: PaymentTransactionResponse[];
}
