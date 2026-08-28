import { CurrencyPipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { PaymentTransactionResponse } from '../../../core/models/ledger.models';
import { ModalBase } from '../../../shared/modal-base';

/**
 * Directive 2 (Refinement Sprint): a dedicated "Payment Details" view of one already-logged
 * payment -- tender info plus the itemized statutory-waterfall allocation breakdown
 * (PaymentAllocation rows written at LogPayment time). Pure display: the payment object is
 * already loaded on the statement page (see UnitStatement.paymentsById), so this never issues
 * its own request.
 */
@Component({
  selector: 'app-payment-details-modal',
  imports: [TranslatePipe, CurrencyPipe],
  templateUrl: './payment-details-modal.html',
})
export class PaymentDetailsModal extends ModalBase {
  readonly payment = input.required<PaymentTransactionResponse>();
}
