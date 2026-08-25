import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/auth.models';
import {
  BatchAssignMatrixRequest,
  PropertyMatrixRowResponse,
  UnitGroupResponse,
  UnitTierResponse,
  UpdatePropertyMatrixRowRequest,
  UpsertUnitGroupRequest,
  UpsertUnitTierRequest,
} from '../models/unit-matrix.models';

/** US-29: UnitTier/UnitGroup catalog CRUD plus the matrix editor's row-level and batch
 * update calls against PropertiesController. */
@Injectable({ providedIn: 'root' })
export class UnitMatrixService {
  private readonly http = inject(HttpClient);

  listUnitTiers(): Observable<UnitTierResponse[]> {
    return this.http
      .get<ApiResponse<UnitTierResponse[]>>('/api/unit-tiers')
      .pipe(map((response) => response.data!));
  }

  createUnitTier(request: UpsertUnitTierRequest): Observable<UnitTierResponse> {
    return this.http
      .post<ApiResponse<UnitTierResponse>>('/api/unit-tiers', request)
      .pipe(map((response) => response.data!));
  }

  updateUnitTier(id: string, request: UpsertUnitTierRequest): Observable<UnitTierResponse> {
    return this.http
      .put<ApiResponse<UnitTierResponse>>(`/api/unit-tiers/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  deleteUnitTier(id: string): Observable<void> {
    return this.http.delete<void>(`/api/unit-tiers/${id}`);
  }

  listUnitGroups(): Observable<UnitGroupResponse[]> {
    return this.http
      .get<ApiResponse<UnitGroupResponse[]>>('/api/unit-groups')
      .pipe(map((response) => response.data!));
  }

  createUnitGroup(request: UpsertUnitGroupRequest): Observable<UnitGroupResponse> {
    return this.http
      .post<ApiResponse<UnitGroupResponse>>('/api/unit-groups', request)
      .pipe(map((response) => response.data!));
  }

  updateUnitGroup(id: string, request: UpsertUnitGroupRequest): Observable<UnitGroupResponse> {
    return this.http
      .put<ApiResponse<UnitGroupResponse>>(`/api/unit-groups/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  deleteUnitGroup(id: string): Observable<void> {
    return this.http.delete<void>(`/api/unit-groups/${id}`);
  }

  updateMatrixRow(propertyId: string, request: UpdatePropertyMatrixRowRequest): Observable<PropertyMatrixRowResponse> {
    return this.http
      .patch<ApiResponse<PropertyMatrixRowResponse>>(`/api/properties/${propertyId}/matrix`, request)
      .pipe(map((response) => response.data!));
  }

  batchAssignMatrix(request: BatchAssignMatrixRequest): Observable<PropertyMatrixRowResponse[]> {
    return this.http
      .patch<ApiResponse<PropertyMatrixRowResponse[]>>('/api/properties/matrix/batch', request)
      .pipe(map((response) => response.data!));
  }
}
