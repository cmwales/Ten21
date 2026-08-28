import { Component, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { RefundTenderTypes, RefundTenderTypeValue } from '../../../core/models/ledger.models';
import { DepositService } from '../../../core/services/deposit.service';
import { ModalBase } from '../../../shared/modal-base';
import { ToastService } from '../../../core/services/toast.service';

/**
 * US-39: "Settle Deposit" -- a single atomic action (the server applies the whole deposit
 * against outstanding charges, then refunds whatever's left, in one call). This modal only
 * captures the TenderType/ReferenceNumber a refund would use if one ends up happening --
 * there's no amount field, since settlement is always "the entire held deposit."
 */
@Component({
  selector: 'app-settle-deposit-modal',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './settle-deposit-modal.html',
})
export class SettleDepositModal extends ModalBase {
  readonly propertyId = input.required<string>();
  readonly depositId = input.required<string>();
  readonly saved = output<void>();

  private readonly fb = inject(FormBuilder);
  private readonly depositService = inject(DepositService);
  private readonly toastService = inject(ToastService);

  protected readonly refundTenderTypes = Object.values(RefundTenderTypes);
  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    tenderType: ['Check' as RefundTenderTypeValue, Validators.required],
    referenceNumber: this.fb.control<string | null>(null),
  });

  protected save(): void {
    if (this.submitting()) {
      return;
    }

    this.submitting.set(true);
    const raw = this.form.getRawValue();
    this.depositService.settleDeposit(this.propertyId(), this.depositId(), raw).subscribe({
      next: (result) => {
        this.submitting.set(false);
        this.toastService.show(
          result.amountRefunded > 0 ? 'deposits.modal.settledWithRefundToast' : 'deposits.modal.settledNoRefundToast',
        );
        this.saved.emit();
        this.closed.emit();
      },
      error: () => {
        this.submitting.set(false);
        this.toastService.show('deposits.modal.errorToast');
      },
    });
  }
}
