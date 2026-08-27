import { Component, OnInit, inject, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkspaceSettingsResponse } from '../../core/models/workspace-settings.models';
import { WorkspaceSettingsService } from '../../core/services/workspace-settings.service';
import { ToastService } from '../../core/services/toast.service';
import { AppHeader } from '../../shared/app-header/app-header';

/**
 * Refinement Sprint (Directive 4): the /admin/settings screen -- workspace-wide admin
 * toggles gated behind Permissions.Workspace.SettingsRead/Write (Property Manager, Board
 * Member, SuperAdmin). Currently one toggle: EnableCommunityDirectory, which
 * WorkspaceSettingsController lazily creates a settings row for on first read.
 */
@Component({
  selector: 'app-admin-settings',
  imports: [TranslatePipe, AppHeader],
  templateUrl: './admin-settings.html',
})
export class AdminSettings implements OnInit {
  private readonly workspaceSettingsService = inject(WorkspaceSettingsService);
  private readonly toastService = inject(ToastService);

  protected readonly settings = signal<WorkspaceSettingsResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly enableCommunityDirectory = signal(true);

  ngOnInit(): void {
    this.workspaceSettingsService.getSettings().subscribe({
      next: (settings) => {
        this.settings.set(settings);
        this.enableCommunityDirectory.set(settings.enableCommunityDirectory);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorKey.set('admin.settings.loadError');
      },
    });
  }

  protected save(): void {
    if (this.saving()) {
      return;
    }

    this.saving.set(true);
    this.workspaceSettingsService.updateSettings({ enableCommunityDirectory: this.enableCommunityDirectory() }).subscribe({
      next: (settings) => {
        this.saving.set(false);
        this.settings.set(settings);
        this.toastService.show('admin.settings.savedToast');
      },
      error: () => {
        this.saving.set(false);
        this.toastService.show('admin.settings.errorToast');
      },
    });
  }
}
