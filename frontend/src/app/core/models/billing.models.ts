/** Mirrors Ten21.Business.Billing.BillingCycleResult (US-44/US-45, Sprint 9). */
export interface BillingCycleResult {
  chargesGenerated: number;
  lateFeesAssessed: number;
}

/** Mirrors Ten21.Domain.Enums.LateFeePolicyType (US-45, Sprint 9). */
export const LateFeePolicyTypes = {
  Flat: 'Flat',
  Percentage: 'Percentage',
  DailyAccruing: 'DailyAccruing',
  Hybrid: 'Hybrid',
} as const;

export type LateFeePolicyTypeValue = (typeof LateFeePolicyTypes)[keyof typeof LateFeePolicyTypes];

/** Mirrors Ten21.Business.Leases.LateFeePolicyRequest/Response (US-45, Sprint 9). */
export interface LateFeePolicyRequest {
  gracePeriodDays: number;
  policyType: LateFeePolicyTypeValue;
  baseAmount: number | null;
  percentageRate: number | null;
  dailyAccrualRate: number | null;
  maxFeeCap: number | null;
}

export interface LateFeePolicyResponse extends LateFeePolicyRequest {
  id: string;
  leaseId: string;
}
