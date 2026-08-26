import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/auth.models';
import { WorkspaceLedgerResponse } from '../models/workspace-ledger.models';

/** US-36: the workspace-wide ledger rollup call. */
@Injectable({ providedIn: 'root' })
export class WorkspaceLedgerService {
  private readonly http = inject(HttpClient);

  getWorkspaceLedger(): Observable<WorkspaceLedgerResponse> {
    return this.http
      .get<ApiResponse<WorkspaceLedgerResponse>>('/api/workspace/ledger')
      .pipe(map((response) => response.data!));
  }
}
