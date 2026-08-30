import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/auth.models';
import { DirectoryAdminResponse, DirectoryEntryResponse } from '../models/directory.models';

/** US-25: the dual-consent community directory. Deliberately no propertyId parameter --
 * the caller's own occupancy scopes the query server-side (see DirectoryController's own
 * comment), so there's nothing for this call to parameterize either. */
@Injectable({ providedIn: 'root' })
export class DirectoryService {
  private readonly http = inject(HttpClient);

  getDirectory(): Observable<DirectoryEntryResponse[]> {
    return this.http
      .get<ApiResponse<DirectoryEntryResponse[]>>('/api/directory')
      .pipe(map((response) => response.data!));
  }

  /** PM-facing verification view -- see DirectoryController.GetDirectoryAdmin's own comment. */
  getDirectoryAdmin(): Observable<DirectoryAdminResponse> {
    return this.http
      .get<ApiResponse<DirectoryAdminResponse>>('/api/directory/admin')
      .pipe(map((response) => response.data!));
  }
}
