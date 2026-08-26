import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { ResidentResponse } from '../../../core/models/resident.models';
import { TenderTypes, TenderTypeValue } from '../../../core/models/ledger.models';
import { PaymentService } from '../../../core/services/payment.service';
import { ResidentService } from '../../../core/services/resident.service';
import { ToastService } from '../../../core/services/toast.service';

/**
 * US-34: the "Log Payment" quick action deferred from US-33 -- captures a manually-received
 * payment (ResidentProfileId, PaymentDate, AmountPaid, TenderType, ReferenceNumber, Notes) and
 * hands it to the server, which runs the statutory waterfall allocation against the unit's
 * outstanding charges. There's no per-CHARGE picker here on purpose -- the PM never chooses
 * which charge the money applies to, the server does (see
 * PaymentsController.BuildWaterfallAllocationsAsync) -- but a resident picker IS required
 * (fix, post-US-34): money belongs to a specific payee, needed so an overpayment or a
 * pre-charge payment can be refunded to the right person later. See
 * PaymentTransaction's own class comment for the full reasoning.
 * Purely a form (unlike ChargeModal's list+form combo) since the unit statement page already
 * renders the payment history right below where this opens from.
 */
@Component({
  selector: 'app-log-payment-modal',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './log-payment-modal.html',
})
export class LogPaymentModal implements OnChanges {
  @Input({ required: true }) propertyId!: string;
  @Input() open = false;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  private readonly fb = inject(FormBuilder);
  private readonly paymentService = inject(PaymentService);
  private readonly residentService = inject(ResidentService);
  private readonly toastService = inject(ToastService);

  protected readonly tenderTypes = Object.values(TenderTypes);
  protected readonly residents = signal<ResidentResponse[]>([]);
  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    residentProfileId: ['', Validators.required],
    paymentDate: ['', Validators.required],
    amountPaid: [0, [Validators.required, Validators.min(0.01)]],
    tenderType: ['Cash' as TenderTypeValue, Validators.required],
    referenceNumber: this.fb.control<string | null>(null),
    notes: this.fb.control<string | null>(null),
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['open'] && this.open) {
      this.residentService.listResidents(this.propertyId).subscribe({
        next: (residents) => this.residents.set(residents),
      });
    }
  }

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
        residentProfileId: raw.residentProfileId,
        paymentDate: raw.paymentDate,
        amountPaid: raw.amountPaid,
        tenderType: raw.tenderType,
        referenceNumber: raw.referenceNumber,
        notes: raw.notes,
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.form.reset({
            residentProfileId: '',
            paymentDate: '',
            amountPaid: 0,
            tenderType: 'Cash' as TenderTypeValue,
            referenceNumber: null,
            notes: null,
          });
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
