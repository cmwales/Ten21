import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/auth.models';
import { ResidentResponse, UpsertResidentRequest } from '../models/resident.models';

/** US-23: resident CRUD calls, nested under a property. Same interceptor-attaches-the-token
 * convention as PropertyService -- no manual auth headers needed here. */
@Injectable({ providedIn: 'root' })
export class ResidentService {
  private readonly http = inject(HttpClient);

  listResidents(propertyId: string): Observable<ResidentResponse[]> {
    return this.http
      .get<ApiResponse<ResidentResponse[]>>(`/api/properties/${propertyId}/residents`)
      .pipe(map((response) => response.data!));
  }

  createResident(propertyId: string, request: UpsertResidentRequest): Observable<ResidentResponse> {
    return this.http
      .post<ApiResponse<ResidentResponse>>(`/api/properties/${propertyId}/residents`, request)
      .pipe(map((response) => response.data!));
  }

  updateResident(propertyId: string, residentId: string, request: UpsertResidentRequest): Observable<ResidentResponse> {
    return this.http
      .put<ApiResponse<ResidentResponse>>(`/api/properties/${propertyId}/residents/${residentId}`, request)
      .pipe(map((response) => response.data!));
  }

  deleteResident(propertyId: string, residentId: string): Observable<void> {
    return this.http.delete<void>(`/api/properties/${propertyId}/residents/${residentId}`);
  }
}
