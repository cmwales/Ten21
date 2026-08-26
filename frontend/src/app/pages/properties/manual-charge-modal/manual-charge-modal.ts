import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { ManualChargeResponse } from '../../../core/models/manual-charge.models';
import { ManualChargeService } from '../../../core/services/manual-charge.service';
import { ToastService } from '../../../core/services/toast.service';

/**
 * US-31: the "Quick-Action Charge Modal" on /properties/:id -- lists this property's one-time
 * charges/fines and lets a PM post a new one. Persisting is the entire "immediate balance
 * impact" this story calls for -- there is no running-balance display yet (Phase 1's payment
 * ledger is still pending), this just establishes the open line-item record a future balance
 * calculation would sum. Centered modal, not a slide-out drawer, per the acceptance
 * criteria's own "Quick-Action Charge Modal" wording.
 *
 * Post-Sprint-6 fix, tester feedback: dropped the per-resident "bill to" -- charges are
 * billed to the unit, not an individual occupant. Added an inline, auto-saving PaidDate
 * control per row (same UX convention as the matrix editor) so a PM can record the actual
 * payment date, which can legitimately differ from whenever they got around to entering it.
 */
@Component({
  selector: 'app-manual-charge-modal',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './manual-charge-modal.html',
})
export class ManualChargeModal implements OnChanges {
  @Input({ required: true }) propertyId!: string;
  @Input() open = false;
  @Output() closed = new EventEmitter<void>();

  private readonly fb = inject(FormBuilder);
  private readonly manualChargeService = inject(ManualChargeService);
  private readonly toastService = inject(ToastService);

  protected readonly charges = signal<ManualChargeResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly submitting = signal(false);
  protected readonly showForm = signal(false);
  protected readonly savingPaidDateForChargeId = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    description: ['', [Validators.required, Validators.maxLength(200)]],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    dueDate: ['', Validators.required],
    accountingCode: this.fb.control<string | null>(null),
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['open'] && this.open) {
      this.loadCharges();
    }
  }

  protected close(): void {
    this.showForm.set(false);
    this.closed.emit();
  }

  protected startAdd(): void {
    this.form.reset({ description: '', amount: 0, dueDate: '', accountingCode: null });
    this.showForm.set(true);
  }

  protected cancelForm(): void {
    this.showForm.set(false);
  }

  protected save(): void {
    if (this.submitting() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    const raw = this.form.getRawValue();
    this.manualChargeService
      .createManualCharge(this.propertyId, {
        description: raw.description,
        amount: raw.amount,
        dueDate: raw.dueDate,
        accountingCode: raw.accountingCode,
        paidDate: null,
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.showForm.set(false);
          this.toastService.show('manualCharges.modal.addedToast');
          this.loadCharges();
        },
        error: () => {
          this.submitting.set(false);
          this.toastService.show('manualCharges.modal.errorToast');
        },
      });
  }

  protected markPaidToday(charge: ManualChargeResponse): void {
    this.setPaidDate(charge, new Date().toISOString().substring(0, 10));
  }

  protected onPaidDateChange(charge: ManualChargeResponse, value: string): void {
    this.setPaidDate(charge, value || null);
  }

  private setPaidDate(charge: ManualChargeResponse, paidDate: string | null): void {
    this.savingPaidDateForChargeId.set(charge.id);
    this.manualChargeService
      .updateManualCharge(this.propertyId, charge.id, {
        description: charge.description,
        amount: charge.amount,
        dueDate: charge.dueDate,
        accountingCode: charge.accountingCode,
        paidDate,
      })
      .subscribe({
        next: (updated) => {
          this.savingPaidDateForChargeId.set(null);
          this.charges.update((current) => current.map((c) => (c.id === updated.id ? updated : c)));
        },
        error: () => {
          this.savingPaidDateForChargeId.set(null);
          this.toastService.show('manualCharges.modal.errorToast');
        },
      });
  }

  protected deleteCharge(charge: ManualChargeResponse): void {
    this.manualChargeService.deleteManualCharge(this.propertyId, charge.id).subscribe({
      next: () => {
        this.toastService.show('manualCharges.modal.removedToast');
        this.loadCharges();
      },
      error: () => this.toastService.show('manualCharges.modal.errorToast'),
    });
  }

  private loadCharges(): void {
    this.loading.set(true);
    this.manualChargeService.listManualCharges(this.propertyId).subscribe({
      next: (charges) => {
        this.charges.set(charges);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
