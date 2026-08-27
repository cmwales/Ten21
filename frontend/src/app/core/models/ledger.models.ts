import { ChargeResponse } from './charge.models';

/** Mirrors Ten21.Domain.Enums.TenderType. */
export const TenderTypes = {
  Cash: 'Cash',
  Check: 'Check',
  Zelle: 'Zelle',
  Venmo: 'Venmo',
  DirectDeposit: 'DirectDeposit',
  CreditCard: 'CreditCard',
} as const;

export type TenderTypeValue = (typeof TenderTypes)[keyof typeof TenderTypes];

/** Mirrors Ten21.Domain.Enums.AdjustmentType. */
export const AdjustmentTypes = {
  CreditAdjustment: 'CreditAdjustment',
  DebitAdjustment: 'DebitAdjustment',
} as const;

export type AdjustmentTypeValue = (typeof AdjustmentTypes)[keyof typeof AdjustmentTypes];

/** Mirrors Ten21.Domain.Enums.RefundTenderType. Narrower than TenderType (no Cash/Venmo) --
 * an outbound refund is a traceable disbursement. */
export const RefundTenderTypes = {
  Check: 'Check',
  DirectDeposit: 'DirectDeposit',
  Zelle: 'Zelle',
} as const;

export type RefundTenderTypeValue = (typeof RefundTenderTypes)[keyof typeof RefundTenderTypes];

/** Mirrors Ten21.Domain.Enums.RefundReason. */
export const RefundReasons = {
  DepositReturn: 'DepositReturn',
  OverpaymentRefund: 'OverpaymentRefund',
} as const;

export type RefundReasonValue = (typeof RefundReasons)[keyof typeof RefundReasons];

/** Mirrors Ten21.Domain.Enums.PaymentTransactionStatus. */
export const PaymentTransactionStatuses = {
  Cleared: 'Cleared',
  Reversed: 'Reversed',
} as const;

export type PaymentTransactionStatusValue = (typeof PaymentTransactionStatuses)[keyof typeof PaymentTransactionStatuses];

/** Mirrors Ten21.Domain.Enums.SecurityDepositStatus. */
export const SecurityDepositStatuses = {
  Held: 'Held',
  Settled: 'Settled',
} as const;

export type SecurityDepositStatusValue = (typeof SecurityDepositStatuses)[keyof typeof SecurityDepositStatuses];

/** Mirrors Ten21.Domain.Enums.AccountStatus. */
export const AccountStatuses = {
  Active: 'Active',
  TerminatedWithBalance: 'TerminatedWithBalance',
} as const;

export type AccountStatusValue = (typeof AccountStatuses)[keyof typeof AccountStatuses];

/** Mirrors Ten21.Domain.Enums.StatementDateRange. */
export const StatementDateRanges = {
  Lifetime: 'Lifetime',
  YearToDate: 'YearToDate',
  Last12Months: 'Last12Months',
} as const;

export type StatementDateRangeValue = (typeof StatementDateRanges)[keyof typeof StatementDateRanges];

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
 * and per-resident history), even though Charges stay unit-scoped. unallocatedAmount (US-37)
 * is this payment's own retained credit, drawn down over time. status/reversalReason/
 * reallocatedToId (US-38) -- a Reversed payment's allocations always come back empty. */
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
  unallocatedAmount: number;
  status: PaymentTransactionStatusValue;
  reversalReason: string | null;
  reallocatedToId: string | null;
  allocations: PaymentAllocationSummaryResponse[];
}

/** Mirrors Ten21.Api.Contracts.Charges.CreditAllocationResponse (US-37) -- a later draw-down
 * of retained credit against a charge, distinct from a payment's own original allocations. */
export interface CreditAllocationResponse {
  id: string;
  sourcePaymentTransactionId: string;
  targetChargeId: string;
  chargeDescription: string;
  appliedAmount: number;
  appliedDate: string;
}

/** Mirrors Ten21.Api.Contracts.Credits.RefundTransactionResponse (US-37). */
export interface RefundTransactionResponse {
  id: string;
  residentProfileId: string;
  residentName: string;
  propertyId: string;
  amount: number;
  refundDate: string;
  tenderType: RefundTenderTypeValue;
  referenceNumber: string | null;
  reason: RefundReasonValue;
  createdAt: string;
}

/** Mirrors Ten21.Api.Contracts.Deposits.SecurityDepositResponse (US-39). */
export interface SecurityDepositResponse {
  id: string;
  propertyId: string;
  residentProfileId: string;
  residentName: string;
  originalAmount: number;
  amountHeld: number;
  collectedDate: string;
  status: SecurityDepositStatusValue;
}

/** Mirrors Ten21.Api.Contracts.Charges.UnitStatementResponse -- the whole "lifetime
 * financial statement" for one unit: charges (with nested adjustments), payments, refunds,
 * and deposits, plus the dynamic running Balance. availableCredit (US-37) is the more
 * specific "how much retained credit is currently un-drawn-down" figure -- see that field's
 * own backend comment. accountStatus (US-39) is TerminatedWithBalance once a Settled deposit
 * couldn't fully cover what was owed. */
export interface UnitStatementResponse {
  propertyId: string;
  balance: number;
  availableCredit: number;
  accountStatus: AccountStatusValue;
  charges: ChargeStatementItemResponse[];
  payments: PaymentTransactionResponse[];
  credits: CreditAllocationResponse[];
  refunds: RefundTransactionResponse[];
  deposits: SecurityDepositResponse[];
  transactionLines: UnitStatementTransactionLineResponse[];
}

/** Mirrors Ten21.Api.Contracts.Charges.UnitStatementTransactionLineResponse (Refinement
 * Sprint) -- Charges and Payments merged into one chronological (oldest-first) timeline with
 * a per-line running balance. referenceId is the Charge.id or PaymentTransactionResponse.id,
 * used to look up the already-loaded rich object for rendering. */
export interface UnitStatementTransactionLineResponse {
  type: 'Charge' | 'Payment';
  date: string;
  referenceId: string;
  runningBalance: number;
}
