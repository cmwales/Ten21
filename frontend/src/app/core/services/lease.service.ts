import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/auth.models';
import { ManualChargeResponse } from '../models/manual-charge.models';
import { CreateMoveInChargeRequest, LeaseResponse, UpsertLeaseRequest } from '../models/lease.models';

/** US-30: lease CRUD calls, nested under a property. Same interceptor-attaches-the-token
 * convention as PropertyService/ResidentService -- no manual auth headers needed here. */
@Injectable({ providedIn: 'root' })
export class LeaseService {
  private readonly http = inject(HttpClient);

  listLeases(propertyId: string): Observable<LeaseResponse[]> {
    return this.http
      .get<ApiResponse<LeaseResponse[]>>(`/api/properties/${propertyId}/leases`)
      .pipe(map((response) => response.data!));
  }

  createLease(propertyId: string, request: UpsertLeaseRequest): Observable<LeaseResponse> {
    return this.http
      .post<ApiResponse<LeaseResponse>>(`/api/properties/${propertyId}/leases`, request)
      .pipe(map((response) => response.data!));
  }

  updateLease(propertyId: string, leaseId: string, request: UpsertLeaseRequest): Observable<LeaseResponse> {
    return this.http
      .put<ApiResponse<LeaseResponse>>(`/api/properties/${propertyId}/leases/${leaseId}`, request)
      .pipe(map((response) => response.data!));
  }

  deleteLease(propertyId: string, leaseId: string): Observable<void> {
    return this.http.delete<void>(`/api/properties/${propertyId}/leases/${leaseId}`);
  }

  /** US-32: "Create Move-In Charge" -- generates a one-time pro-rated charge covering the
   * partial period from moveInDate through the day before the lease's next regular billing
   * anchor. Returns the created ManualCharge (see LeasesController's own doc comment for why
   * this reuses that shape instead of a separate ProRatedCharge type). */
  createMoveInCharge(propertyId: string, leaseId: string, request: CreateMoveInChargeRequest): Observable<ManualChargeResponse> {
    return this.http
      .post<ApiResponse<ManualChargeResponse>>(`/api/properties/${propertyId}/leases/${leaseId}/move-in-charge`, request)
      .pipe(map((response) => response.data!));
  }
}
