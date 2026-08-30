import { Component, OnInit, inject, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { DirectoryAdminEntryResponse } from '../../core/models/directory.models';
import { DirectoryService } from '../../core/services/directory.service';
import { AppHeader } from '../../shared/app-header/app-header';

/**
 * PM-facing verification view of the community directory: "what is currently showing up,"
 * across the whole workspace -- a bare list, deliberately no links out to the underlying
 * Property/Resident records (the PM-facing ask was specifically "just a list," not another
 * navigation surface). See DirectoryController.GetDirectoryAdmin's own comment for why this
 * is a separate endpoint/permission from the Tenant-facing /directory page rather than a
 * reuse of it.
 */
@Component({
  selector: 'app-directory-admin',
  imports: [TranslatePipe, AppHeader],
  templateUrl: './directory-admin.html',
})
export class DirectoryAdmin implements OnInit {
  private readonly directoryService = inject(DirectoryService);

  protected readonly entries = signal<DirectoryAdminEntryResponse[]>([]);
  protected readonly workspaceDirectoryEnabled = signal(true);
  protected readonly loading = signal(true);
  protected readonly errorKey = signal<string | null>(null);

  ngOnInit(): void {
    this.directoryService.getDirectoryAdmin().subscribe({
      next: (response) => {
        this.entries.set(response.entries);
        this.workspaceDirectoryEnabled.set(response.workspaceDirectoryEnabled);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorKey.set('directoryAdmin.loadError');
      },
    });
  }
}
