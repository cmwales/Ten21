import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { RefundTenderTypes, RefundTenderTypeValue } from '../../../core/models/ledger.models';
import { ResidentResponse } from '../../../core/models/resident.models';
import { CreditService } from '../../../core/services/credit.service';
import { ResidentService } from '../../../core/services/resident.service';
import { ToastService } from '../../../core/services/toast.service';

/**
 * US-37: "Refund Credit Balance" -- disburses some or all of a resident's retained
 * overpayment credit back to them via a RefundTransaction. The server rejects the request
 * outright if the amount exceeds what that resident actually has available (a 409), so this
 * modal doesn't try to duplicate that check client-side beyond basic required-field validation.
 */
@Component({
  selector: 'app-refund-credit-modal',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './refund-credit-modal.html',
})
export class RefundCreditModal implements OnChanges {
  @Input({ required: true }) propertyId!: string;
  @Input() open = false;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  private readonly fb = inject(FormBuilder);
  private readonly creditService = inject(CreditService);
  private readonly residentService = inject(ResidentService);
  private readonly toastService = inject(ToastService);

  protected readonly refundTenderTypes = Object.values(RefundTenderTypes);
  protected readonly residents = signal<ResidentResponse[]>([]);
  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    residentProfileId: ['', Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    refundDate: ['', Validators.required],
    tenderType: ['Check' as RefundTenderTypeValue, Validators.required],
    referenceNumber: this.fb.control<string | null>(null),
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
    this.creditService
      .refundCreditBalance(this.propertyId, {
        residentProfileId: raw.residentProfileId,
        amount: raw.amount,
        refundDate: raw.refundDate,
        tenderType: raw.tenderType,
        referenceNumber: raw.referenceNumber,
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.form.reset({
            residentProfileId: '',
            amount: 0,
            refundDate: '',
            tenderType: 'Check' as RefundTenderTypeValue,
            referenceNumber: null,
          });
          this.toastService.show('refunds.modal.addedToast');
          this.saved.emit();
          this.closed.emit();
        },
        error: (err) => {
          this.submitting.set(false);
          this.toastService.show(err.status === 409 ? 'refunds.modal.insufficientCreditToast' : 'refunds.modal.errorToast');
        },
      });
  }
}
