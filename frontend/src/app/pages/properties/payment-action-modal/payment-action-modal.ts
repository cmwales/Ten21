import { Component, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { PropertyListItemDto } from '../../../core/models/property.models';
import { ResidentResponse } from '../../../core/models/resident.models';
import { PaymentService } from '../../../core/services/payment.service';
import { PropertyService } from '../../../core/services/property.service';
import { ResidentService } from '../../../core/services/resident.service';
import { ModalBase } from '../../../shared/modal-base';
import { ToastService } from '../../../core/services/toast.service';

/**
 * US-38: "Reverse Payment" (NSF/bounced) and "Reallocate Payment" (cross-property posting
 * error) share one modal since they're both "what actually happened to this payment"
 * corrections with a mandatory reason -- Reallocate just adds a required target
 * property/resident on top. Only offered on a Cleared payment (see unit-statement.html);
 * once reversed, a payment has nothing left to reverse or reallocate again.
 */
@Component({
  selector: 'app-payment-action-modal',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './payment-action-modal.html',
})
export class PaymentActionModal extends ModalBase {
  readonly propertyId = input.required<string>();
  readonly paymentId = input.required<string>();
  readonly saved = output<void>();

  private readonly fb = inject(FormBuilder);
  private readonly paymentService = inject(PaymentService);
  private readonly propertyService = inject(PropertyService);
  private readonly residentService = inject(ResidentService);
  private readonly toastService = inject(ToastService);

  protected readonly properties = signal<PropertyListItemDto[]>([]);
  protected readonly targetResidents = signal<ResidentResponse[]>([]);
  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    actionType: ['reverse' as 'reverse' | 'reallocate', Validators.required],
    reversalReason: ['', [Validators.required, Validators.maxLength(250)]],
    targetPropertyId: [''],
    targetResidentProfileId: [''],
  });

  protected override onOpen(): void {
    this.form.reset({ actionType: 'reverse', reversalReason: '', targetPropertyId: '', targetResidentProfileId: '' });
    this.targetResidents.set([]);
    this.propertyService.listProperties().subscribe({
      next: (result) => this.properties.set(result.items.filter((p) => p.id !== this.propertyId())),
    });
  }

  protected onTargetPropertyChange(): void {
    const targetPropertyId = this.form.controls.targetPropertyId.value;
    this.form.controls.targetResidentProfileId.setValue('');
    this.targetResidents.set([]);
    if (targetPropertyId) {
      this.residentService.listResidents(targetPropertyId).subscribe({
        next: (residents) => this.targetResidents.set(residents),
      });
    }
  }

  protected save(): void {
    const raw = this.form.getRawValue();
    const isReallocate = raw.actionType === 'reallocate';

    if (this.submitting() || this.form.controls.reversalReason.invalid || (isReallocate && (!raw.targetPropertyId || !raw.targetResidentProfileId))) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    const request$ = isReallocate
      ? this.paymentService.reallocatePayment(this.propertyId(), this.paymentId(), {
          targetPropertyId: raw.targetPropertyId,
          targetResidentProfileId: raw.targetResidentProfileId,
          reversalReason: raw.reversalReason,
        })
      : this.paymentService.reversePayment(this.propertyId(), this.paymentId(), { reversalReason: raw.reversalReason });

    request$.subscribe({
      next: () => {
        this.submitting.set(false);
        this.toastService.show(isReallocate ? 'payments.action.reallocatedToast' : 'payments.action.reversedToast');
        this.saved.emit();
        this.closed.emit();
      },
      error: () => {
        this.submitting.set(false);
        this.toastService.show('payments.action.errorToast');
      },
    });
  }
}
