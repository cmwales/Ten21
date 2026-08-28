import { CurrencyPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
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
 */
@Component({
  selector: 'app-ledger',
  imports: [RouterLink, TranslatePipe, AppHeader, CurrencyPipe],
  templateUrl: './ledger.html',
})
export class Ledger implements OnInit {
  private readonly workspaceLedgerService = inject(WorkspaceLedgerService);

  protected readonly ledger = signal<WorkspaceLedgerResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorKey = signal<string | null>(null);

  ngOnInit(): void {
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
