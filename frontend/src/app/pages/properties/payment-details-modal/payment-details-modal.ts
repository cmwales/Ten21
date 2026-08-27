import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { PaymentTransactionResponse } from '../../../core/models/ledger.models';

/**
 * Directive 2 (Refinement Sprint): a dedicated "Payment Details" view of one already-logged
 * payment -- tender info plus the itemized statutory-waterfall allocation breakdown
 * (PaymentAllocation rows written at LogPayment time). Pure display: the payment object is
 * already loaded on the statement page (see UnitStatement.paymentsById), so this never issues
 * its own request.
 */
@Component({
  selector: 'app-payment-details-modal',
  imports: [TranslatePipe],
  templateUrl: './payment-details-modal.html',
})
export class PaymentDetailsModal {
  @Input({ required: true }) payment!: PaymentTransactionResponse;
  @Input() open = false;
  @Output() closed = new EventEmitter<void>();

  protected close(): void {
    this.closed.emit();
  }
}
