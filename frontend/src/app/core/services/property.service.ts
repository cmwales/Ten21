import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/auth.models';
import { PropertyResponse, UpsertPropertyRequest } from '../models/property.models';

/** US-19: Property/Unit CRUD calls. The auth interceptor attaches the bearer token to every
 * /api/* request automatically, so no manual headers are needed here (unlike the
 * interim-token flows in AuthService). */
@Injectable({ providedIn: 'root' })
export class PropertyService {
  private readonly http = inject(HttpClient);

  getProperty(id: string): Observable<PropertyResponse> {
    return this.http
      .get<ApiResponse<PropertyResponse>>(`/api/properties/${id}`)
      .pipe(map((response) => response.data!));
  }

  createProperty(request: UpsertPropertyRequest): Observable<PropertyResponse> {
    return this.http
      .post<ApiResponse<PropertyResponse>>('/api/properties', request)
      .pipe(map((response) => response.data!));
  }

  updateProperty(id: string, request: UpsertPropertyRequest): Observable<PropertyResponse> {
    return this.http
      .put<ApiResponse<PropertyResponse>>(`/api/properties/${id}`, request)
      .pipe(map((response) => response.data!));
  }
}
