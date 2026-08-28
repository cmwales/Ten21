import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { DirectoryEntryResponse } from '../../core/models/directory.models';
import { DirectoryService } from '../../core/services/directory.service';
import { AppHeader } from '../../shared/app-header/app-header';

/**
 * US-25: the community directory -- a Tenant-role resident's opted-in neighbors (name + unit
 * only, never contact info). Deliberately the only screen this permission unlocks: a Tenant
 * has no propertyId of their own to pass anywhere, the server resolves "which neighbors" from
 * their own occupancy (see DirectoryController's own comment), so this page is a plain,
 * parameterless list.
 *
 * Audit Refinement Sprint: previously the backend (dual opt-in + the workspace-wide
 * EnableCommunityDirectory toggle) had no Angular UI at all -- no route, no nav link, no
 * component. This is that missing UI. A disabled-workspace-wide 403 is shown as its own
 * friendly state, distinct from a generic load error, since it's an expected, PM-controlled
 * outcome rather than a failure.
 */
@Component({
  selector: 'app-directory',
  imports: [TranslatePipe, AppHeader],
  templateUrl: './directory.html',
})
export class Directory implements OnInit {
  private readonly directoryService = inject(DirectoryService);

  protected readonly entries = signal<DirectoryEntryResponse[]>([]);
  protected readonly loading = signal(true);
  protected readonly disabled = signal(false);
  protected readonly errorKey = signal<string | null>(null);

  ngOnInit(): void {
    this.directoryService.getDirectory().subscribe({
      next: (entries) => {
        this.entries.set(entries);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        if (error instanceof HttpErrorResponse && error.status === 403) {
          this.disabled.set(true);
        } else {
          this.errorKey.set('directory.loadError');
        }
      },
    });
  }
}
