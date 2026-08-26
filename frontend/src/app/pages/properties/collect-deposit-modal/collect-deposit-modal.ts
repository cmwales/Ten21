import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { ResidentResponse } from '../../../core/models/resident.models';
import { DepositService } from '../../../core/services/deposit.service';
import { ResidentService } from '../../../core/services/resident.service';
import { ToastService } from '../../../core/services/toast.service';

/**
 * US-39: "Collect Deposit" -- logged at move-in. ResidentProfileId is deliberately left
 * selectable-or-blank (unlike LogPaymentModal's required picker): per the sprint's
 * "Dual-Anchor Attribution" rule, leaving it blank tells the server to auto-default to the
 * Primary Resident on the unit's active lease.
 */
@Component({
  selector: 'app-collect-deposit-modal',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './collect-deposit-modal.html',
})
export class CollectDepositModal implements OnChanges {
  @Input({ required: true }) propertyId!: string;
  @Input() open = false;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  private readonly fb = inject(FormBuilder);
  private readonly depositService = inject(DepositService);
  private readonly residentService = inject(ResidentService);
  private readonly toastService = inject(ToastService);

  protected readonly residents = signal<ResidentResponse[]>([]);
  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    amount: [0, [Validators.required, Validators.min(0.01)]],
    collectedDate: ['', Validators.required],
    residentProfileId: [''],
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['open'] && this.open) {
      this.form.reset({ amount: 0, collectedDate: '', residentProfileId: '' });
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
    this.depositService
      .collectDeposit(this.propertyId, {
        amount: raw.amount,
        collectedDate: raw.collectedDate,
        residentProfileId: raw.residentProfileId || null,
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.toastService.show('deposits.modal.collectedToast');
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
