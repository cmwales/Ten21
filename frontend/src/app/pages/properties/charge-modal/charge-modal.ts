import { CurrencyPipe } from '@angular/common';
import { Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { ChargeCategories, ChargeCategoryValue, ChargeResponse } from '../../../core/models/charge.models';
import { AdjustmentTypes, AdjustmentTypeValue } from '../../../core/models/ledger.models';
import { ChargeService } from '../../../core/services/charge.service';
import { ModalBase } from '../../../shared/modal-base';
import { ToastService } from '../../../core/services/toast.service';

/**
 * Renamed from ManualChargeModal (Sprint 7): the "Quick-Action Charge Modal" on
 * /properties/:id -- lists this property's charges (fines, add-ons, and now also
 * manually-posted rent -- see Charge's own class comment for why there's no more separate
 * "manual" charge concept) and lets a PM post a new one, categorized for the US-34 statutory
 * waterfall. PaymentStatus (Unpaid/Partial/Paid) is shown read-only, computed server-side
 * from actual logged payments -- there is no more inline PaidDate control here; recording a
 * real payment happens via the "Log Payment" action on the unit statement page (US-34), which
 * properly allocates across charges instead of just flipping one charge to "paid."
 *
 * US-35: an unlocked (unpaid) charge can be Removed (hard-gone) or Voided (stays visible,
 * badged, excluded from balance -- see VoidCharge's own comment for the distinction); once
 * locked, neither works and the only remaining action is Adjust, which posts a signed
 * ChargeAdjustment with a mandatory reason instead of touching the charge's own Amount.
 */
@Component({
  selector: 'app-charge-modal',
  imports: [ReactiveFormsModule, TranslatePipe, CurrencyPipe],
  templateUrl: './charge-modal.html',
})
export class ChargeModal extends ModalBase {
  readonly propertyId = input.required<string>();

  private readonly fb = inject(FormBuilder);
  private readonly chargeService = inject(ChargeService);
  private readonly toastService = inject(ToastService);

  protected readonly chargeCategories = Object.values(ChargeCategories);
  protected readonly adjustmentTypes = Object.values(AdjustmentTypes);
  protected readonly charges = signal<ChargeResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly submitting = signal(false);
  protected readonly showForm = signal(false);
  protected readonly adjustingCharge = signal<ChargeResponse | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    description: ['', [Validators.required, Validators.maxLength(200)]],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    dueDate: ['', Validators.required],
    accountingCode: this.fb.control<string | null>(null),
    category: ['AddOn' as ChargeCategoryValue, Validators.required],
    notes: this.fb.control<string | null>(null, Validators.maxLength(500)),
  });

  protected readonly adjustmentForm = this.fb.nonNullable.group({
    adjustmentType: ['CreditAdjustment' as AdjustmentTypeValue, Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    reason: ['', [Validators.required, Validators.maxLength(500)]],
  });

  protected override onOpen(): void {
    this.loadCharges();
  }

  protected override close(): void {
    this.showForm.set(false);
    this.adjustingCharge.set(null);
    super.close();
  }

  protected startAdd(): void {
    this.form.reset({ description: '', amount: 0, dueDate: '', accountingCode: null, category: 'AddOn' as ChargeCategoryValue, notes: null });
    this.showForm.set(true);
  }

  protected cancelForm(): void {
    this.showForm.set(false);
  }

  protected startAdjust(charge: ChargeResponse): void {
    this.adjustmentForm.reset({ adjustmentType: 'CreditAdjustment' as AdjustmentTypeValue, amount: 0, reason: '' });
    this.adjustingCharge.set(charge);
  }

  protected cancelAdjust(): void {
    this.adjustingCharge.set(null);
  }

  protected saveAdjustment(): void {
    if (this.submitting() || this.adjustmentForm.invalid) {
      this.adjustmentForm.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    const raw = this.adjustmentForm.getRawValue();
    this.chargeService.createAdjustment(this.propertyId(), this.adjustingCharge()!.id, raw).subscribe({
      next: () => {
        this.submitting.set(false);
        this.adjustingCharge.set(null);
        this.toastService.show('charges.modal.adjustedToast');
        this.loadCharges();
      },
      error: () => {
        this.submitting.set(false);
        this.toastService.show('charges.modal.errorToast');
      },
    });
  }

  protected voidCharge(charge: ChargeResponse): void {
    this.chargeService.voidCharge(this.propertyId(), charge.id).subscribe({
      next: () => {
        this.toastService.show('charges.modal.voidedToast');
        this.loadCharges();
      },
      error: () => this.toastService.show('charges.modal.lockedErrorToast'),
    });
  }

  protected save(): void {
    if (this.submitting() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    const raw = this.form.getRawValue();
    this.chargeService
      .createCharge(this.propertyId(), {
        description: raw.description,
        amount: raw.amount,
        dueDate: raw.dueDate,
        accountingCode: raw.accountingCode,
        category: raw.category,
        notes: raw.notes,
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.showForm.set(false);
          this.toastService.show('charges.modal.addedToast');
          this.loadCharges();
        },
        error: () => {
          this.submitting.set(false);
          this.toastService.show('charges.modal.errorToast');
        },
      });
  }

  protected deleteCharge(charge: ChargeResponse): void {
    this.chargeService.deleteCharge(this.propertyId(), charge.id).subscribe({
      next: () => {
        this.toastService.show('charges.modal.removedToast');
        this.loadCharges();
      },
      error: () => this.toastService.show('charges.modal.lockedErrorToast'),
    });
  }

  private loadCharges(): void {
    this.loading.set(true);
    this.chargeService.listCharges(this.propertyId()).subscribe({
      next: (charges) => {
        this.charges.set(charges);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
