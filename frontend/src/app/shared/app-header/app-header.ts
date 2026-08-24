import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { RoleNames } from '../../core/constants/roles';
import { TenantMembershipSummary } from '../../core/models/organization.models';
import { AuthService } from '../../core/services/auth.service';

/**
 * US-28: the app's shared top-nav header -- extracted from what used to be duplicated
 * inline markup on dashboard.html, extended with the workspace switcher dropdown. Rendered
 * once per authenticated page rather than each page rolling its own header, so the switcher
 * (and any future nav change) is consistent everywhere instead of needing to be added
 * page-by-page.
 */
@Component({
  selector: 'app-header',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './app-header.html',
})
export class AppHeader implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly isTenant = () => this.authService.role() === RoleNames.Tenant;
  protected readonly canManageProperties = () => {
    const role = this.authService.role();
    return role !== null && role !== RoleNames.Tenant && role !== RoleNames.Vendor;
  };

  protected readonly workspaces = signal<TenantMembershipSummary[]>([]);
  protected readonly workspaceMenuOpen = signal(false);
  protected readonly switching = signal(false);

  protected readonly activeWorkspaceName = computed(() => {
    const tenantId = this.authService.tenantId();
    return this.workspaces().find((w) => w.tenantId === tenantId)?.tenantName ?? null;
  });

  /** Every OTHER workspace -- the active one is shown as the trigger itself, not repeated
   * inside its own dropdown. */
  protected readonly otherWorkspaces = computed(() => {
    const tenantId = this.authService.tenantId();
    return this.workspaces().filter((w) => w.tenantId !== tenantId);
  });

  ngOnInit(): void {
    this.loadWorkspaces();
  }

  protected toggleWorkspaceMenu(): void {
    this.workspaceMenuOpen.update((open) => !open);
  }

  protected closeWorkspaceMenu(): void {
    this.workspaceMenuOpen.set(false);
  }

  protected switchWorkspace(tenantId: string): void {
    if (this.switching()) {
      return;
    }

    this.switching.set(true);
    this.authService.switchWorkspace(tenantId).subscribe({
      next: () => {
        this.switching.set(false);
        this.workspaceMenuOpen.set(false);
        this.loadWorkspaces();
        this.refreshCurrentRoute();
      },
      error: () => {
        this.switching.set(false);
      },
    });
  }

  protected logout(): void {
    this.authService.logout().subscribe(() => void this.router.navigateByUrl('/login'));
  }

  private loadWorkspaces(): void {
    this.authService.listWorkspaces().subscribe({
      next: (workspaces) => this.workspaces.set(workspaces),
    });
  }

  /** Reactive route-data refresh without a browser reload: re-entering the current route
   * re-runs the routed component's own ngOnInit (and any resolvers), so a page that fetched
   * its data once (e.g. property-list) picks up the newly-switched tenant's data instead of
   * silently keeping the old workspace's results on screen. skipLocationChange avoids
   * leaving a junk '/' entry in browser history. */
  private refreshCurrentRoute(): void {
    const currentUrl = this.router.url;
    void this.router.navigateByUrl('/', { skipLocationChange: true }).then(() => {
      void this.router.navigateByUrl(currentUrl);
    });
  }
}
