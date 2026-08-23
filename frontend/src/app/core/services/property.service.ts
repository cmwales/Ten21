import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/auth.models';
import { PropertyListResponse, PropertyResponse, UpsertPropertyRequest } from '../models/property.models';

/** US-19: Property/Unit CRUD calls. The auth interceptor attaches the bearer token to every
 * /api/* request automatically, so no manual headers are needed here (unlike the
 * interim-token flows in AuthService). */
@Injectable({ providedIn: 'root' })
export class PropertyService {
  private readonly http = inject(HttpClient);

  /** US-20: fetches every property (no pageNumber/pageSize passed) -- the list page does
   * its own client-side search/pagination over the full set, since a debounced search can't
   * see rows outside whatever page a server-paginated query returned. */
  listProperties(): Observable<PropertyListResponse> {
    return this.http
      .get<ApiResponse<PropertyListResponse>>('/api/properties')
      .pipe(map((response) => response.data!));
  }

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
