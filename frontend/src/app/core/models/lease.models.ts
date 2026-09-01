import { ChargeCategoryValue } from './charge.models';

/** Mirrors Ten21.Domain.Enums.LeaseStatus. */
export const LeaseStatuses = {
  FixedTerm: 'FixedTerm',
  MonthToMonth: 'MonthToMonth',
  Ended: 'Ended',
} as const;

export type LeaseStatusValue = (typeof LeaseStatuses)[keyof typeof LeaseStatuses];

/** Mirrors Ten21.Domain.Enums.RecurrencePattern (US-44, Sprint 9). */
export const RecurrencePatterns = {
  Daily: 'Daily',
  Weekly: 'Weekly',
  BiWeekly: 'BiWeekly',
  SemiMonthly: 'SemiMonthly',
  Monthly: 'Monthly',
  Custom: 'Custom',
} as const;

export type RecurrencePatternValue = (typeof RecurrencePatterns)[keyof typeof RecurrencePatterns];

/** Mirrors Ten21.Domain.Enums.EndStrategy (US-44, Sprint 9). */
export const EndStrategies = {
  Indefinite: 'Indefinite',
  FixedDate: 'FixedDate',
  LeaseAligned: 'LeaseAligned',
} as const;

export type EndStrategyValue = (typeof EndStrategies)[keyof typeof EndStrategies];

/** Mirrors Ten21.Domain.Enums.ProrationStrategy (US-44, Sprint 9). */
export const ProrationStrategies = {
  FullAmount: 'FullAmount',
  ZeroFirstMonth: 'ZeroFirstMonth',
  ProRateByDays: 'ProRateByDays',
} as const;

export type ProrationStrategyValue = (typeof ProrationStrategies)[keyof typeof ProrationStrategies];

/** Mirrors Ten21.Domain.Enums.DayOfWeek (.NET's numbering: Sunday = 0). */
export const DaysOfWeek = {
  Sunday: 0,
  Monday: 1,
  Tuesday: 2,
  Wednesday: 3,
  Thursday: 4,
  Friday: 5,
  Saturday: 6,
} as const;

/** Mirrors Ten21.Business.Leases.LeaseRecurringChargeRequest (US-44, Sprint 9: base rent
 * is now just a row here with category = BaseRent, unified with every add-on). */
export interface LeaseRecurringChargeRequest {
  chargeName: string;
  category: ChargeCategoryValue;
  amount: number;
  recurrencePattern: RecurrencePatternValue;
  endStrategy: EndStrategyValue;
  effectiveStartDate: string;
  prorationStrategy: ProrationStrategyValue;
  accountingCode: string | null;
  description: string | null;
  recurrenceInterval: number;
  dueDayOfMonth: number | null;
  targetDayOfWeek: number | null;
  secondaryDueDay: number | null;
  effectiveEndDate: string | null;
  isPaused: boolean;
}

/** Mirrors Ten21.Business.Leases.LeaseRecurringChargeResponse. */
export interface LeaseRecurringChargeResponse extends LeaseRecurringChargeRequest {
  id: string;
}

/** Mirrors Ten21.Business.Leases.UpsertLeaseRequest. Dates are ISO 'yyyy-MM-dd' strings
 * (DateOnly on the wire). US-44: RecurringCharges must contain exactly one
 * category = BaseRent row -- base rent is no longer a separate field here. */
export interface UpsertLeaseRequest {
  residentId: string;
  startDate: string;
  endDate: string;
  recurringCharges: LeaseRecurringChargeRequest[];
  status: LeaseStatusValue;
}

/** Mirrors Ten21.Business.Leases.LeaseResponse. TotalMonthlyDues is computed
 * server-side (Sum of every currently-active RecurringCharges row), never stored. US-32:
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
