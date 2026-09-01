import { CurrencyPipe, NgTemplateOutlet } from '@angular/common';
import { Component, inject, input, signal } from '@angular/core';
import { FormArray, FormBuilder, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { ChargeCategories, ChargeCategoryValue } from '../../../core/models/charge.models';
import {
  EndStrategies,
  EndStrategyValue,
  LeaseRecurringChargeRequest,
  LeaseRecurringChargeResponse,
  LeaseResponse,
  LeaseStatusValue,
  LeaseStatuses,
  ProrationStrategies,
  ProrationStrategyValue,
  RecurrencePatterns,
  RecurrencePatternValue,
} from '../../../core/models/lease.models';
import { ResidentResponse } from '../../../core/models/resident.models';
import { LeaseService } from '../../../core/services/lease.service';
import { PropertyService } from '../../../core/services/property.service';
import { ResidentService } from '../../../core/services/resident.service';
import { ModalBase } from '../../../shared/modal-base';
import { ToastService } from '../../../core/services/toast.service';

interface RecurringChargeControls {
  chargeName: import('@angular/forms').FormControl<string>;
  category: import('@angular/forms').FormControl<ChargeCategoryValue>;
  amount: import('@angular/forms').FormControl<number>;
  recurrencePattern: import('@angular/forms').FormControl<RecurrencePatternValue>;
  recurrenceInterval: import('@angular/forms').FormControl<number>;
  dueDayOfMonth: import('@angular/forms').FormControl<number | null>;
  targetDayOfWeek: import('@angular/forms').FormControl<number | null>;
  secondaryDueDay: import('@angular/forms').FormControl<number | null>;
  endStrategy: import('@angular/forms').FormControl<EndStrategyValue>;
  effectiveStartDate: import('@angular/forms').FormControl<string>;
  effectiveEndDate: import('@angular/forms').FormControl<string | null>;
  prorationStrategy: import('@angular/forms').FormControl<ProrationStrategyValue>;
  isPaused: import('@angular/forms').FormControl<boolean>;
  accountingCode: import('@angular/forms').FormControl<string | null>;
  description: import('@angular/forms').FormControl<string | null>;
}

interface LeaseFormControls {
  residentId: import('@angular/forms').FormControl<string>;
  startDate: import('@angular/forms').FormControl<string>;
  endDate: import('@angular/forms').FormControl<string>;
  status: import('@angular/forms').FormControl<LeaseStatusValue>;
  recurringCharges: FormArray<FormGroup<RecurringChargeControls>>;
}

/**
 * US-30: the slide-out Lease drawer on /properties/:id -- lists this property's leases and
 * lets a PM attach a resident to the unit with contract dates and recurring charges. Same
 * shape/conventions as ResidentDrawer. Only meaningful once a Property already exists, so
 * PropertyFormContainer only renders/opens this in edit mode, never on /properties/new.
 *
 * US-44 (Sprint 9): base rent is no longer a separate pair of fields -- it's row 0 of
 * recurringCharges, category = BaseRent, pinned first and non-removable (every lease needs
 * exactly one, enforced server-side too). Every row now carries the full recurring-charge
 * template shape (recurrence pattern, due day, effective window, proration strategy,
 * paused toggle) instead of just a name/amount/accounting code.
 */
@Component({
  selector: 'app-lease-drawer',
  imports: [ReactiveFormsModule, FormsModule, TranslatePipe, CurrencyPipe, NgTemplateOutlet],
  templateUrl: './lease-drawer.html',
})
export class LeaseDrawer extends ModalBase {
  readonly propertyId = input.required<string>();

  private readonly fb = inject(FormBuilder);
  private readonly leaseService = inject(LeaseService);
  private readonly residentService = inject(ResidentService);
  private readonly propertyService = inject(PropertyService);
  private readonly toastService = inject(ToastService);

  protected readonly leaseStatuses = Object.values(LeaseStatuses);
  protected readonly recurrencePatterns = Object.values(RecurrencePatterns);
  protected readonly endStrategies = Object.values(EndStrategies);
  protected readonly prorationStrategies = Object.values(ProrationStrategies);
  protected readonly addOnCategories = [ChargeCategories.AddOn, ChargeCategories.SpecialAssessment];
  protected readonly leases = signal<LeaseResponse[]>([]);
  protected readonly residents = signal<ResidentResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly submitting = signal(false);
  protected readonly showForm = signal(false);
  protected readonly editingLeaseId = signal<string | null>(null);
  protected readonly moveInChargeLeaseId = signal<string | null>(null);
  protected readonly moveInDate = signal('');
  protected readonly creatingMoveInCharge = signal(false);

  /** Post-Sprint-6 fix: property-level move-out notice -- see PropertyResponse's own doc
   * comment for why this moved off Lease. Loaded/saved alongside the lease list, not
   * per-lease. */
  protected readonly propertyMoveOutNoticeDate = signal<string | null>(null);
  protected readonly savingMoveOutNotice = signal(false);

  protected form: FormGroup<LeaseFormControls> = this.buildForm();

  /** US-30 AC: "Calculates total recurring dues... dynamically on the form UI" -- recomputed
   * from the form's own live value on every relevant keystroke (see recomputeTotal), not a
   * value round-tripped from the server. */
  protected readonly liveTotalMonthlyDues = signal(0);

  protected override onOpen(): void {
    this.loadLeases();
    this.loadResidents();
    this.loadPropertyMoveOutNotice();
  }

  protected onMoveOutNoticeChange(value: string): void {
    this.savingMoveOutNotice.set(true);
    this.propertyService.updateMoveOutNotice(this.propertyId(), { moveOutNoticeDate: value || null }).subscribe({
      next: (property) => {
        this.savingMoveOutNotice.set(false);
        this.propertyMoveOutNoticeDate.set(property.moveOutNoticeDate ?? null);
        this.toastService.show('leases.drawer.moveOutNoticeSavedToast');
      },
      error: () => {
        this.savingMoveOutNotice.set(false);
        this.toastService.show('leases.drawer.errorToast');
      },
    });
  }

  private loadPropertyMoveOutNotice(): void {
    this.propertyService.getProperty(this.propertyId()).subscribe({
      next: (property) => this.propertyMoveOutNoticeDate.set(property.moveOutNoticeDate ?? null),
    });
  }

  protected override close(): void {
    this.showForm.set(false);
    super.close();
  }

  protected startAdd(): void {
    this.form = this.buildForm();
    this.addRecurringChargeRow(ChargeCategories.BaseRent, 'Base Rent');
    this.recomputeTotal();
    this.editingLeaseId.set(null);
    this.showForm.set(true);
  }

  protected startEdit(lease: LeaseResponse): void {
    this.form = this.buildForm();
    // Base Rent first, always -- row 0 is pinned/non-removable in the template below.
    const sorted = [...lease.recurringCharges].sort((a) =>
      a.category === ChargeCategories.BaseRent ? -1 : 1,
    );
    sorted.forEach((charge) => this.addRecurringChargeRow(charge.category, charge.chargeName, charge));
    this.form.patchValue({
      residentId: lease.residentId,
      startDate: lease.startDate.substring(0, 10),
      endDate: lease.endDate.substring(0, 10),
      status: lease.status,
    });
    this.recomputeTotal();
    this.editingLeaseId.set(lease.id);
    this.showForm.set(true);
  }

  protected cancelForm(): void {
    this.showForm.set(false);
    this.editingLeaseId.set(null);
  }

  protected addRecurringChargeRow(
    category: ChargeCategoryValue = ChargeCategories.AddOn,
    chargeName = '',
    existing?: LeaseRecurringChargeResponse,
  ): void {
    const today = new Date().toISOString().substring(0, 10);
    this.form.controls.recurringCharges.push(
      this.fb.nonNullable.group({
        chargeName: [existing?.chargeName ?? chargeName, Validators.required],
        category: this.fb.nonNullable.control<ChargeCategoryValue>(existing?.category ?? category),
        amount: [existing?.amount ?? 0, [Validators.required, Validators.min(0)]],
        recurrencePattern: this.fb.nonNullable.control<RecurrencePatternValue>(
          existing?.recurrencePattern ?? RecurrencePatterns.Monthly,
        ),
        recurrenceInterval: [existing?.recurrenceInterval ?? 1, [Validators.required, Validators.min(1)]],
        dueDayOfMonth: this.fb.control<number | null>(existing?.dueDayOfMonth ?? 1, [Validators.min(1), Validators.max(31)]),
        targetDayOfWeek: this.fb.control<number | null>(existing?.targetDayOfWeek ?? null),
        secondaryDueDay: this.fb.control<number | null>(existing?.secondaryDueDay ?? null, [
          Validators.min(1),
          Validators.max(31),
        ]),
        endStrategy: this.fb.nonNullable.control<EndStrategyValue>(existing?.endStrategy ?? EndStrategies.LeaseAligned),
        effectiveStartDate: [existing?.effectiveStartDate?.substring(0, 10) ?? today, Validators.required],
        effectiveEndDate: this.fb.control<string | null>(existing?.effectiveEndDate?.substring(0, 10) ?? null),
        prorationStrategy: this.fb.nonNullable.control<ProrationStrategyValue>(
          existing?.prorationStrategy ?? ProrationStrategies.FullAmount,
        ),
        isPaused: this.fb.nonNullable.control(existing?.isPaused ?? false),
        accountingCode: this.fb.control<string | null>(existing?.accountingCode ?? null),
        description: this.fb.control<string | null>(existing?.description ?? null),
      }),
    );
    this.recomputeTotal();
  }

  /** Row 0 (Base Rent) is pinned -- every lease needs exactly one, enforced server-side too. */
  protected removeRecurringChargeRow(index: number): void {
    if (index === 0) {
      return;
    }
    this.form.controls.recurringCharges.removeAt(index);
    this.recomputeTotal();
  }

  /** Called on (input) from every recurring charge amount field -- see
   * liveTotalMonthlyDues's own comment for why this is a plain recompute rather than a
   * computed() over the form (the form itself is a mutable property, reassigned wholesale
   * by startAdd/startEdit, not a signal computed() could track). */
  protected recomputeTotal(): void {
    const raw = this.form.getRawValue();
    this.liveTotalMonthlyDues.set(raw.recurringCharges.reduce((sum, c) => sum + (Number(c.amount) || 0), 0));
  }

  protected save(): void {
    if (this.submitting() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    const raw = this.form.getRawValue();
    const request = {
      residentId: raw.residentId,
      startDate: raw.startDate,
      endDate: raw.endDate,
      status: raw.status,
      recurringCharges: raw.recurringCharges.map(
        (c): LeaseRecurringChargeRequest => ({
          chargeName: c.chargeName,
          category: c.category,
          amount: c.amount,
          recurrencePattern: c.recurrencePattern,
          recurrenceInterval: c.recurrenceInterval,
          dueDayOfMonth: c.dueDayOfMonth,
          targetDayOfWeek: c.targetDayOfWeek,
          secondaryDueDay: c.secondaryDueDay,
          endStrategy: c.endStrategy,
          effectiveStartDate: c.effectiveStartDate,
          effectiveEndDate: c.effectiveEndDate,
          prorationStrategy: c.prorationStrategy,
          isPaused: c.isPaused,
          accountingCode: c.accountingCode,
          description: c.description,
        }),
      ),
    };

    const leaseId = this.editingLeaseId();
    const call = leaseId
      ? this.leaseService.updateLease(this.propertyId(), leaseId, request)
      : this.leaseService.createLease(this.propertyId(), request);

    call.subscribe({
      next: () => {
        this.submitting.set(false);
        this.showForm.set(false);
        this.editingLeaseId.set(null);
        this.toastService.show(leaseId ? 'leases.drawer.savedToast' : 'leases.drawer.addedToast');
        this.loadLeases();
      },
      error: () => {
        this.submitting.set(false);
        this.toastService.show('leases.drawer.errorToast');
      },
    });
  }

  protected deleteLease(lease: LeaseResponse): void {
    this.leaseService.deleteLease(this.propertyId(), lease.id).subscribe({
      next: () => {
        this.toastService.show('leases.drawer.removedToast');
        this.loadLeases();
      },
      error: () => this.toastService.show('leases.drawer.errorToast'),
    });
  }

  protected startMoveInCharge(lease: LeaseResponse): void {
    this.moveInChargeLeaseId.set(lease.id);
    this.moveInDate.set(lease.startDate.substring(0, 10));
  }

  protected cancelMoveInCharge(): void {
    this.moveInChargeLeaseId.set(null);
  }

  protected createMoveInCharge(lease: LeaseResponse): void {
    if (this.creatingMoveInCharge()) {
      return;
    }

    this.creatingMoveInCharge.set(true);
    this.leaseService.createMoveInCharge(this.propertyId(), lease.id, { moveInDate: this.moveInDate() }).subscribe({
      next: () => {
        this.creatingMoveInCharge.set(false);
        this.moveInChargeLeaseId.set(null);
        // Amount/description are visible in the Charges & Fines modal (US-31) -- the created
        // record IS a Charge, no separate confirmation view needed.
        this.toastService.show('leases.drawer.moveInChargeCreatedToast');
      },
      error: () => {
        this.creatingMoveInCharge.set(false);
        this.toastService.show('leases.drawer.errorToast');
      },
    });
  }

  /** Excludes the pinned Base Rent row -- this badge means "additional recurring add-on
   * charges," same meaning it had before US-44 unified base rent into recurringCharges. */
  protected addOnCount(lease: LeaseResponse): number {
    return lease.recurringCharges.filter((c) => c.category !== ChargeCategories.BaseRent).length;
  }

  protected residentName(residentId: string): string {
    const resident = this.residents().find((r) => r.id === residentId);
    return resident ? `${resident.firstName} ${resident.lastName}` : residentId;
  }

  private loadLeases(): void {
    this.loading.set(true);
    this.leaseService.listLeases(this.propertyId()).subscribe({
      next: (leases) => {
        this.leases.set(leases);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  private loadResidents(): void {
    this.residentService.listResidents(this.propertyId()).subscribe({
      next: (residents) => this.residents.set(residents),
    });
  }

  private buildForm(): FormGroup<LeaseFormControls> {
    return this.fb.nonNullable.group({
      residentId: ['', Validators.required],
      startDate: ['', Validators.required],
      endDate: ['', Validators.required],
      status: ['FixedTerm' as LeaseStatusValue, Validators.required],
      recurringCharges: this.fb.array<FormGroup<RecurringChargeControls>>([]),
    });
  }
}
