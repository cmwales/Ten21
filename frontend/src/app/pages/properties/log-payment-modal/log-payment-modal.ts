import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { TenderTypes, TenderTypeValue } from '../../../core/models/ledger.models';
import { PaymentService } from '../../../core/services/payment.service';
import { ToastService } from '../../../core/services/toast.service';

/**
 * US-34: the "Log Payment" quick action deferred from US-33 -- captures a manually-received
 * payment (PaymentDate, AmountPaid, TenderType, ReferenceNumber, Notes) and hands it to the
 * server, which runs the statutory waterfall allocation. There's no per-charge picker here on
 * purpose: "we dont really care who made the payment" / the PM never chooses which charge it
 * applies to -- the server does that (see PaymentsController.BuildWaterfallAllocationsAsync).
 * Purely a form (unlike ChargeModal's list+form combo) since the unit statement page already
 * renders the payment history right below where this opens from.
 */
@Component({
  selector: 'app-log-payment-modal',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './log-payment-modal.html',
})
export class LogPaymentModal {
  @Input({ required: true }) propertyId!: string;
  @Input() open = false;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  private readonly fb = inject(FormBuilder);
  private readonly paymentService = inject(PaymentService);
  private readonly toastService = inject(ToastService);

  protected readonly tenderTypes = Object.values(TenderTypes);
  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    paymentDate: ['', Validators.required],
    amountPaid: [0, [Validators.required, Validators.min(0.01)]],
    tenderType: ['Cash' as TenderTypeValue, Validators.required],
    referenceNumber: this.fb.control<string | null>(null),
    notes: this.fb.control<string | null>(null),
  });

  protected close(): void {
    this.closed.emit();
  }

  protected save(): void {
    if (this.submitting() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    const raw = this.form.getRawValue();
    this.paymentService
      .logPayment(this.propertyId, {
        paymentDate: raw.paymentDate,
        amountPaid: raw.amountPaid,
        tenderType: raw.tenderType,
        referenceNumber: raw.referenceNumber,
        notes: raw.notes,
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.form.reset({ paymentDate: '', amountPaid: 0, tenderType: 'Cash' as TenderTypeValue, referenceNumber: null, notes: null });
          this.toastService.show('payments.modal.addedToast');
          this.saved.emit();
          this.closed.emit();
        },
        error: () => {
          this.submitting.set(false);
          this.toastService.show('payments.modal.errorToast');
        },
      });
  }
}
