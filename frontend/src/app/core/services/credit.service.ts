import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/auth.models';
import { CreditAllocationResponse, RefundTenderTypeValue, RefundTransactionResponse } from '../models/ledger.models';

/** US-37: "Apply Credits to Charges" (a manual, PM-triggered button -- not a scheduled job,
 * see CreditsController's own comment) and "Refund Credit Balance". */
@Injectable({ providedIn: 'root' })
export class CreditService {
  private readonly http = inject(HttpClient);

  applyCreditsToCharges(propertyId: string): Observable<ApplyCreditsResponse> {
    return this.http
      .post<ApiResponse<ApplyCreditsResponse>>(`/api/properties/${propertyId}/credits/apply`, {})
      .pipe(map((response) => response.data!));
  }

  refundCreditBalance(propertyId: string, request: RefundCreditBalanceRequest): Observable<RefundTransactionResponse> {
    return this.http
      .post<ApiResponse<RefundTransactionResponse>>(`/api/properties/${propertyId}/refunds`, request)
      .pipe(map((response) => response.data!));
  }
}

/** Mirrors Ten21.Api.Contracts.Credits.ApplyCreditsResponse. */
export interface ApplyCreditsResponse {
  totalApplied: number;
  allocations: CreditAllocationResponse[];
}

/** Mirrors Ten21.Api.Contracts.Credits.RefundCreditBalanceRequest. */
export interface RefundCreditBalanceRequest {
  residentProfileId: string;
  amount: number;
  refundDate: string;
  tenderType: RefundTenderTypeValue;
  referenceNumber: string | null;
}
