import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/auth.models';
import { ChargeResponse, UpsertChargeRequest } from '../models/charge.models';
import { UnitStatementResponse } from '../models/ledger.models';

/** Renamed from ManualChargeService (Sprint 7): charge CRUD calls, nested under a property,
 * plus the unit's full financial statement. Same interceptor-attaches-the-token convention
 * as PropertyService/LeaseService. */
@Injectable({ providedIn: 'root' })
export class ChargeService {
  private readonly http = inject(HttpClient);

  listCharges(propertyId: string): Observable<ChargeResponse[]> {
    return this.http
      .get<ApiResponse<ChargeResponse[]>>(`/api/properties/${propertyId}/charges`)
      .pipe(map((response) => response.data!));
  }

  createCharge(propertyId: string, request: UpsertChargeRequest): Observable<ChargeResponse> {
    return this.http
      .post<ApiResponse<ChargeResponse>>(`/api/properties/${propertyId}/charges`, request)
      .pipe(map((response) => response.data!));
  }

  updateCharge(propertyId: string, chargeId: string, request: UpsertChargeRequest): Observable<ChargeResponse> {
    return this.http
      .put<ApiResponse<ChargeResponse>>(`/api/properties/${propertyId}/charges/${chargeId}`, request)
      .pipe(map((response) => response.data!));
  }

  deleteCharge(propertyId: string, chargeId: string): Observable<void> {
    return this.http.delete<void>(`/api/properties/${propertyId}/charges/${chargeId}`);
  }

  /** US-33: the unit's full financial statement -- charges (with nested adjustments),
   * payments, and the dynamic running balance. */
  getStatement(propertyId: string): Observable<UnitStatementResponse> {
    return this.http
      .get<ApiResponse<UnitStatementResponse>>(`/api/properties/${propertyId}/charges/statement`)
      .pipe(map((response) => response.data!));
  }
}
