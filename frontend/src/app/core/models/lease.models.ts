/** Mirrors Ten21.Domain.Enums.LeaseStatus. */
export const LeaseStatuses = {
  FixedTerm: 'FixedTerm',
  MonthToMonth: 'MonthToMonth',
  Ended: 'Ended',
} as const;

export type LeaseStatusValue = (typeof LeaseStatuses)[keyof typeof LeaseStatuses];

/** Mirrors Ten21.Api.Contracts.Leases.LeaseRecurringChargeRequest. */
export interface LeaseRecurringChargeRequest {
  chargeName: string;
  amount: number;
  accountingCode: string | null;
}

/** Mirrors Ten21.Api.Contracts.Leases.LeaseRecurringChargeResponse. */
export interface LeaseRecurringChargeResponse {
  id: string;
  chargeName: string;
  amount: number;
  accountingCode: string | null;
}

/** Mirrors Ten21.Api.Contracts.Leases.UpsertLeaseRequest. Dates are ISO 'yyyy-MM-dd' strings
 * (DateOnly on the wire). */
export interface UpsertLeaseRequest {
  residentId: string;
  startDate: string;
  endDate: string;
  monthlyBaseRent: number;
  dueDayOfMonth: number;
  moveOutNoticeDate: string | null;
  recurringCharges: LeaseRecurringChargeRequest[];
  status: LeaseStatusValue;
}

/** Mirrors Ten21.Api.Contracts.Leases.LeaseResponse. TotalMonthlyDues is computed
 * server-side (MonthlyBaseRent + Sum(RecurringCharges)), never stored. */
export interface LeaseResponse {
  id: string;
  propertyId: string;
  residentId: string;
  startDate: string;
  endDate: string;
  monthlyBaseRent: number;
  dueDayOfMonth: number;
  status: LeaseStatusValue;
  moveOutNoticeDate: string | null;
  totalMonthlyDues: number;
  recurringCharges: LeaseRecurringChargeResponse[];
}
