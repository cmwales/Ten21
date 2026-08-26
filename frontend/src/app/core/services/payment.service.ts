import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/auth.models';
import { PaymentTransactionResponse, TenderTypeValue } from '../models/ledger.models';

/** US-34: logging a manually-received payment against a property; the server runs the
 * statutory waterfall allocation and returns the resulting per-charge allocations. Same
 * interceptor-attaches-the-token convention as ChargeService. */
@Injectable({ providedIn: 'root' })
export class PaymentService {
  private readonly http = inject(HttpClient);

  logPayment(propertyId: string, request: LogPaymentRequest): Observable<PaymentTransactionResponse> {
    return this.http
      .post<ApiResponse<PaymentTransactionResponse>>(`/api/properties/${propertyId}/payments`, request)
      .pipe(map((response) => response.data!));
  }

  /** US-38: NSF/bounced -- un-links this payment's allocations and restores the charges it
   * touched, but never deletes the row itself. */
  reversePayment(propertyId: string, paymentId: string, request: ReversePaymentRequest): Observable<PaymentTransactionResponse> {
    return this.http
      .post<ApiResponse<PaymentTransactionResponse>>(`/api/properties/${propertyId}/payments/${paymentId}/reverse`, request)
      .pipe(map((response) => response.data!));
  }

  /** US-38: cross-property posting error -- reverses this payment and atomically creates a
   * linked replacement under the correct property/resident. Returns the NEW payment. */
  reallocatePayment(propertyId: string, paymentId: string, request: ReallocatePaymentRequest): Observable<PaymentTransactionResponse> {
    return this.http
      .post<ApiResponse<PaymentTransactionResponse>>(`/api/properties/${propertyId}/payments/${paymentId}/reallocate`, request)
      .pipe(map((response) => response.data!));
  }

  /** US-40: raw PDF bytes, not an ApiResponse-wrapped call -- see ChargeService.getStatementPdf's
   * own comment. */
  getReceipt(propertyId: string, paymentId: string): Observable<Blob> {
    return this.http.get(`/api/properties/${propertyId}/payments/${paymentId}/receipt`, { responseType: 'blob' });
  }
}

/** Mirrors Ten21.Api.Contracts.Charges.LogPaymentRequest. residentProfileId is required --
 * see PaymentTransactionResponse's own comment for why. */
export interface LogPaymentRequest {
  residentProfileId: string;
  paymentDate: string;
  amountPaid: number;
  tenderType: TenderTypeValue;
  referenceNumber: string | null;
  notes: string | null;
}

/** Mirrors Ten21.Api.Contracts.Charges.ReversePaymentRequest. */
export interface ReversePaymentRequest {
  reversalReason: string;
}

/** Mirrors Ten21.Api.Contracts.Charges.ReallocatePaymentRequest. */
export interface ReallocatePaymentRequest {
  targetPropertyId: string;
  targetResidentProfileId: string;
  reversalReason: string;
}
