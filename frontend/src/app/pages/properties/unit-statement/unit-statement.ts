import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { PropertyResponse } from '../../../core/models/property.models';
import { ChargeService } from '../../../core/services/charge.service';
import { UnitStatementResponse } from '../../../core/models/ledger.models';
import { PropertyService } from '../../../core/services/property.service';
import { AppHeader } from '../../../shared/app-header/app-header';

/**
 * US-33: the "lifetime financial statement screen" for one unit -- every charge (with its
 * adjustments nested directly beneath it), every logged payment, and the dynamic running
 * Balance. In this codebase's flattened Property model (one Property row = one door/unit,
 * see Property's own class comment), a unit's statement and "the property's ledger" are the
 * same thing -- so this page also serves as US-36's per-property ledger view, at the exact
 * route (/properties/:id/ledger) that story names.
 *
 * The "Log Payment" quick action the acceptance criteria calls for ships with US-34
 * (Multi-Tender Manual Payment Entry), not here -- a button with no working modal behind it
 * yet would be a half-finished feature; the trigger and its form are one cohesive unit best
 * shipped together.
 */
@Component({
  selector: 'app-unit-statement',
  imports: [TranslatePipe, RouterLink, AppHeader],
  templateUrl: './unit-statement.html',
})
export class UnitStatement implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly chargeService = inject(ChargeService);
  private readonly propertyService = inject(PropertyService);

  protected readonly propertyId = signal('');
  protected readonly property = signal<PropertyResponse | null>(null);
  protected readonly statement = signal<UnitStatementResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorKey = signal<string | null>(null);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.errorKey.set('ledger.statement.loadError');
      this.loading.set(false);
      return;
    }

    this.propertyId.set(id);
    this.propertyService.getProperty(id).subscribe({
      next: (property) => this.property.set(property),
    });
    this.chargeService.getStatement(id).subscribe({
      next: (statement) => {
        this.statement.set(statement);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorKey.set('ledger.statement.loadError');
      },
    });
  }
}
