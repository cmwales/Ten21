import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { ManualChargeResponse } from '../../../core/models/manual-charge.models';
import { ResidentResponse } from '../../../core/models/resident.models';
import { ManualChargeService } from '../../../core/services/manual-charge.service';
import { ResidentService } from '../../../core/services/resident.service';
import { ToastService } from '../../../core/services/toast.service';

/**
 * US-31: the "Quick-Action Charge Modal" on /properties/:id -- lists this property's one-time
 * charges/fines and lets a PM post a new one, scoped either to the unit generally or to a
 * specific resident. Persisting is the entire "immediate balance impact" this story calls
 * for -- there is no running-balance display yet (Phase 1's payment ledger is still pending),
 * this just establishes the open line-item record a future balance calculation would sum.
 * Centered modal, not a slide-out drawer, per the acceptance criteria's own "Quick-Action
 * Charge Modal" wording.
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
  private readonly residentService = inject(ResidentService);
  private readonly toastService = inject(ToastService);

  protected readonly charges = signal<ManualChargeResponse[]>([]);
  protected readonly residents = signal<ResidentResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly submitting = signal(false);
  protected readonly showForm = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    residentId: this.fb.control<string | null>(null),
    description: ['', [Validators.required, Validators.maxLength(200)]],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    dueDate: ['', Validators.required],
    accountingCode: this.fb.control<string | null>(null),
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['open'] && this.open) {
      this.loadCharges();
      this.loadResidents();
    }
  }

  protected close(): void {
    this.showForm.set(false);
    this.closed.emit();
  }

  protected startAdd(): void {
    this.form.reset({ residentId: null, description: '', amount: 0, dueDate: '', accountingCode: null });
    this.showForm.set(true);
  }

  protected cancelForm(): void {
    this.showForm.set(false);
  }

  protected residentName(residentId: string | null): string | null {
    if (residentId === null) {
      return null;
    }
    const resident = this.residents().find((r) => r.id === residentId);
    return resident ? `${resident.firstName} ${resident.lastName}` : residentId;
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
        residentId: raw.residentId,
        description: raw.description,
        amount: raw.amount,
        dueDate: raw.dueDate,
        accountingCode: raw.accountingCode,
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

  private loadResidents(): void {
    this.residentService.listResidents(this.propertyId).subscribe({
      next: (residents) => this.residents.set(residents),
    });
  }
}
