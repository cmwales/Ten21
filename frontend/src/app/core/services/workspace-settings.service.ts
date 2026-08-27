import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/auth.models';
import { UpdateWorkspaceSettingsRequest, WorkspaceSettingsResponse } from '../models/workspace-settings.models';

/** Refinement Sprint (Directive 4): the /admin/settings backend -- workspace-wide admin
 * toggles, currently just EnableCommunityDirectory. */
@Injectable({ providedIn: 'root' })
export class WorkspaceSettingsService {
  private readonly http = inject(HttpClient);

  getSettings(): Observable<WorkspaceSettingsResponse> {
    return this.http
      .get<ApiResponse<WorkspaceSettingsResponse>>('/api/workspace/settings')
      .pipe(map((response) => response.data!));
  }

  updateSettings(request: UpdateWorkspaceSettingsRequest): Observable<WorkspaceSettingsResponse> {
    return this.http
      .put<ApiResponse<WorkspaceSettingsResponse>>('/api/workspace/settings', request)
      .pipe(map((response) => response.data!));
  }
}
