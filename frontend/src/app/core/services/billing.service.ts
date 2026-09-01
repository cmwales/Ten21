import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/auth.models';
import { BillingCycleResult } from '../models/billing.models';

/** US-44 (Sprint 9): the tenant-wide recurring charge/late fee cycle -- runs for every
 * property/lease the caller's tenant has in one call, not scoped to a single property. */
@Injectable({ providedIn: 'root' })
export class BillingService {
  private readonly http = inject(HttpClient);

  runCycle(): Observable<BillingCycleResult> {
    return this.http
      .post<ApiResponse<BillingCycleResult>>('/api/billing/run-cycle', {})
      .pipe(map((response) => response.data!));
  }
}
