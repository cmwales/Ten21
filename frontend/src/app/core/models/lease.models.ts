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
  recurringCharges: LeaseRecurringChargeRequest[];
  status: LeaseStatusValue;
}

/** Mirrors Ten21.Api.Contracts.Leases.LeaseResponse. TotalMonthlyDues is computed
 * server-side (MonthlyBaseRent + Sum(RecurringCharges)), never stored. US-32:
 * effectiveStatus/isExpiringSoon are also computed server-side at read time -- status stays
 * the raw stored value, effectiveStatus is what the UI should actually display/badge. The
 * move-out notice that feeds effectiveStatus/isExpiringSoon lives on the Property now, not
 * here -- see PropertyResponse.moveOutNoticeDate -- so it isn't duplicated onto every lease. */
export interface LeaseResponse {
  id: string;
  propertyId: string;
  residentId: string;
  startDate: string;
  endDate: string;
  monthlyBaseRent: number;
  dueDayOfMonth: number;
  status: LeaseStatusValue;
  totalMonthlyDues: number;
  recurringCharges: LeaseRecurringChargeResponse[];
  effectiveStatus: LeaseStatusValue;
  isExpiringSoon: boolean;
}

/** Mirrors Ten21.Api.Contracts.Leases.CreateMoveInChargeRequest (US-32). */
export interface CreateMoveInChargeRequest {
  moveInDate: string;
}
