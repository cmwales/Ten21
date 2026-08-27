import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { PropertyResponse } from '../../../core/models/property.models';
import { ChargeService } from '../../../core/services/charge.service';
import { CreditService } from '../../../core/services/credit.service';
import {
  ChargeStatementItemResponse,
  PaymentTransactionResponse,
  UnitStatementResponse,
} from '../../../core/models/ledger.models';
import { PropertyService } from '../../../core/services/property.service';
import { ToastService } from '../../../core/services/toast.service';
import { AppHeader } from '../../../shared/app-header/app-header';
import { CollectDepositModal } from '../collect-deposit-modal/collect-deposit-modal';
import { LogPaymentModal } from '../log-payment-modal/log-payment-modal';
import { PaymentActionModal } from '../payment-action-modal/payment-action-modal';
import { PaymentDetailsModal } from '../payment-details-modal/payment-details-modal';
import { RefundCreditModal } from '../refund-credit-modal/refund-credit-modal';
import { SettleDepositModal } from '../settle-deposit-modal/settle-deposit-modal';

/**
 * US-33/US-34/US-37: the "lifetime financial statement screen" for one unit -- every charge
 * (with its adjustments nested directly beneath it), every logged payment, credit draw-down,
 * and refund, plus the dynamic running Balance and AvailableCredit. In this codebase's
 * flattened Property model (one Property row = one door/unit, see Property's own class
 * comment), a unit's statement and "the property's ledger" are the same thing -- so this page
 * also serves as US-36's per-property ledger view, at the exact route
 * (/properties/:id/ledger) that story names.
 *
 * The "Log Payment" quick action (US-34) opens LogPaymentModal, which posts the payment and
 * lets the server run the statutory waterfall allocation; "Apply Credits to Charges" (US-37)
 * is a single manual button (deliberately not automatic -- see CreditsController's own
 * comment) that runs the drawdown immediately and reports what it did; "Refund Credit
 * Balance" opens RefundCreditModal. All three just reload the statement on success so the
 * updated balance/credit/history show up.
 */
@Component({
  selector: 'app-unit-statement',
  imports: [
    TranslatePipe,
    RouterLink,
    AppHeader,
    LogPaymentModal,
    RefundCreditModal,
    PaymentActionModal,
    PaymentDetailsModal,
    CollectDepositModal,
    SettleDepositModal,
  ],
  templateUrl: './unit-statement.html',
})
export class UnitStatement implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly chargeService = inject(ChargeService);
  private readonly creditService = inject(CreditService);
  private readonly propertyService = inject(PropertyService);
  private readonly toastService = inject(ToastService);

  protected readonly propertyId = signal('');
  protected readonly property = signal<PropertyResponse | null>(null);
  protected readonly statement = signal<UnitStatementResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly logPaymentModalOpen = signal(false);
  protected readonly refundModalOpen = signal(false);
  protected readonly applyingCredits = signal(false);
  protected readonly paymentActionTargetId = signal<string | null>(null);
  protected readonly paymentDetailsTargetId = signal<string | null>(null);
  protected readonly collectDepositModalOpen = signal(false);
  protected readonly settleDepositTargetId = signal<string | null>(null);

  /** Directive 3: lookup maps so the merged chronological transactionLines timeline can pull
   * the already-loaded rich Charge/Payment object it needs to render (adjustments, badges,
   * allocations, actions) without a second request per row. */
  protected readonly chargesById = computed(() => {
    const map = new Map<string, ChargeStatementItemResponse>();
    for (const item of this.statement()?.charges ?? []) {
      map.set(item.charge.id, item);
    }
    return map;
  });

  protected readonly paymentsById = computed(() => {
    const map = new Map<string, PaymentTransactionResponse>();
    for (const payment of this.statement()?.payments ?? []) {
      map.set(payment.id, payment);
    }
    return map;
  });

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
    this.loadStatement(id);
  }

  protected loadStatement(propertyId: string): void {
    this.chargeService.getStatement(propertyId).subscribe({
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

  protected applyCredits(): void {
    this.applyingCredits.set(true);
    this.creditService.applyCreditsToCharges(this.propertyId()).subscribe({
      next: (result) => {
        this.applyingCredits.set(false);
        this.toastService.show(result.totalApplied > 0 ? 'ledger.statement.creditsAppliedToast' : 'ledger.statement.noCreditsAppliedToast');
        this.loadStatement(this.propertyId());
      },
      error: () => {
        this.applyingCredits.set(false);
        this.toastService.show('ledger.statement.loadError');
      },
    });
  }
}
