import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/auth.models';
import { ManualChargeResponse, UpsertManualChargeRequest } from '../models/manual-charge.models';

/** US-31: manual charge/fine CRUD calls, nested under a property. Same
 * interceptor-attaches-the-token convention as PropertyService/LeaseService. */
@Injectable({ providedIn: 'root' })
export class ManualChargeService {
  private readonly http = inject(HttpClient);

  listManualCharges(propertyId: string): Observable<ManualChargeResponse[]> {
    return this.http
      .get<ApiResponse<ManualChargeResponse[]>>(`/api/properties/${propertyId}/manual-charges`)
      .pipe(map((response) => response.data!));
  }

  createManualCharge(propertyId: string, request: UpsertManualChargeRequest): Observable<ManualChargeResponse> {
    return this.http
      .post<ApiResponse<ManualChargeResponse>>(`/api/properties/${propertyId}/manual-charges`, request)
      .pipe(map((response) => response.data!));
  }

  /** Post-Sprint-6 fix: the primary use is recording a PaidDate after the fact (paid by
   * check/cash on one day, entered into the system on another) -- a full replace of the
   * charge, same convention as every other Upsert in this codebase. */
  updateManualCharge(propertyId: string, chargeId: string, request: UpsertManualChargeRequest): Observable<ManualChargeResponse> {
    return this.http
      .put<ApiResponse<ManualChargeResponse>>(`/api/properties/${propertyId}/manual-charges/${chargeId}`, request)
      .pipe(map((response) => response.data!));
  }

  deleteManualCharge(propertyId: string, chargeId: string): Observable<void> {
    return this.http.delete<void>(`/api/properties/${propertyId}/manual-charges/${chargeId}`);
  }
}
