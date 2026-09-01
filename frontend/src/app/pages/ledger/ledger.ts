import { CurrencyPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { BillingService } from '../../core/services/billing.service';
import { ToastService } from '../../core/services/toast.service';
import { WorkspaceLedgerResponse } from '../../core/models/workspace-ledger.models';
import { WorkspaceLedgerService } from '../../core/services/workspace-ledger.service';
import { AppHeader } from '../../shared/app-header/app-header';

/**
 * US-36: the workspace-wide ledger rollup -- every property in the caller's tenant with its
 * own balance, plus the portfolio-wide total. A pure reporting view (no new tables, per the
 * Founder's own framing of this story); each row drills down into that property's own
 * unit statement (US-33, /properties/:id/ledger), which remains the per-property ledger in
 * this codebase's flattened Property model (one row = one door -- see Property's own class
 * comment). Replaces the Phase 0 placeholder that used to live at this same /ledger route
 * (see app.routes.ts's denyRolesGuard gating, unchanged).
 *
 * US-44 (Sprint 9): also hosts the "Run Billing Cycle" manual trigger -- POST
 * api/billing/run-cycle is tenant-wide (every property, not just one), so this
 * portfolio-level page is its natural home rather than a single property's Lease drawer.
 * The button is shown to every viewer of this page; a non-PM caller gets the same 403 the
 * server already enforces (Permissions.Lease.Manage), surfaced via the existing error
 * toast pattern -- no separate client-side permission check exists in this codebase yet.
 */
@Component({
  selector: 'app-ledger',
  imports: [RouterLink, TranslatePipe, AppHeader, CurrencyPipe],
  templateUrl: './ledger.html',
})
export class Ledger implements OnInit {
  private readonly workspaceLedgerService = inject(WorkspaceLedgerService);
  private readonly billingService = inject(BillingService);
  private readonly toastService = inject(ToastService);

  protected readonly ledger = signal<WorkspaceLedgerResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly runningCycle = signal(false);

  ngOnInit(): void {
    this.loadLedger();
  }

  protected runBillingCycle(): void {
    if (this.runningCycle()) {
      return;
    }

    this.runningCycle.set(true);
    this.billingService.runCycle().subscribe({
      next: () => {
        this.runningCycle.set(false);
        this.toastService.show('ledger.runCycleSuccessToast');
        this.loadLedger();
      },
      error: () => {
        this.runningCycle.set(false);
        this.toastService.show('ledger.runCycleErrorToast');
      },
    });
  }

  private loadLedger(): void {
    this.workspaceLedgerService.getWorkspaceLedger().subscribe({
      next: (ledger) => {
        this.ledger.set(ledger);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorKey.set('ledger.loadError');
      },
    });
  }
}
