import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/auth.models';
import { RefundTenderTypeValue, SecurityDepositResponse } from '../models/ledger.models';

/** US-39: security deposit escrow -- collecting at move-in, settling at move-out. */
@Injectable({ providedIn: 'root' })
export class DepositService {
  private readonly http = inject(HttpClient);

  collectDeposit(propertyId: string, request: CollectDepositRequest): Observable<SecurityDepositResponse> {
    return this.http
      .post<ApiResponse<SecurityDepositResponse>>(`/api/properties/${propertyId}/deposits`, request)
      .pipe(map((response) => response.data!));
  }

  /** Applies the whole deposit against outstanding charges, then refunds whatever's left --
   * a single atomic action, not two separate steps. */
  settleDeposit(propertyId: string, depositId: string, request: SettleDepositRequest): Observable<SettleDepositResponse> {
    return this.http
      .post<ApiResponse<SettleDepositResponse>>(`/api/properties/${propertyId}/deposits/${depositId}/settle`, request)
      .pipe(map((response) => response.data!));
  }
}

/** Mirrors Ten21.Api.Contracts.Deposits.CollectDepositRequest. residentProfileId is optional
 * -- leaving it unset auto-defaults to the Primary Resident on the unit's active lease. */
export interface CollectDepositRequest {
  amount: number;
  collectedDate: string;
  residentProfileId: string | null;
}

/** Mirrors Ten21.Api.Contracts.Deposits.SettleDepositRequest. */
export interface SettleDepositRequest {
  tenderType: RefundTenderTypeValue;
  referenceNumber: string | null;
}

/** Mirrors Ten21.Api.Contracts.Deposits.SettleDepositResponse. */
export interface SettleDepositResponse {
  deposit: SecurityDepositResponse;
  amountAppliedToCharges: number;
  amountRefunded: number;
}
