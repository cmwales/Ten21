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
}

/** Mirrors Ten21.Api.Contracts.Charges.LogPaymentRequest. */
export interface LogPaymentRequest {
  paymentDate: string;
  amountPaid: number;
  tenderType: TenderTypeValue;
  referenceNumber: string | null;
  notes: string | null;
}
