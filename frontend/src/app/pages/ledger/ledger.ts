import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';

/**
 * US-13 AC2 proof point: a route reachable only by roles other than the non-owner
 * Tenant (see app.routes.ts's denyRolesGuard([RoleNames.Tenant])). The real ledger UI is
 * BUSINESS_RULES/FEATURES scope beyond Phase 4 — this exists to host the guard, not to
 * be the finished feature.
 */
@Component({
  selector: 'app-ledger',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './ledger.html',
})
export class Ledger {}
