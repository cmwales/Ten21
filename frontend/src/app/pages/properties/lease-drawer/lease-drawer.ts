import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { LeaseResponse, LeaseStatusValue, LeaseStatuses } from '../../../core/models/lease.models';
import { ResidentResponse } from '../../../core/models/resident.models';
import { LeaseService } from '../../../core/services/lease.service';
import { PropertyService } from '../../../core/services/property.service';
import { ResidentService } from '../../../core/services/resident.service';
import { ToastService } from '../../../core/services/toast.service';

interface RecurringChargeControls {
  chargeName: import('@angular/forms').FormControl<string>;
  amount: import('@angular/forms').FormControl<number>;
  accountingCode: import('@angular/forms').FormControl<string | null>;
}

interface LeaseFormControls {
  residentId: import('@angular/forms').FormControl<string>;
  startDate: import('@angular/forms').FormControl<string>;
  endDate: import('@angular/forms').FormControl<string>;
  monthlyBaseRent: import('@angular/forms').FormControl<number>;
  dueDayOfMonth: import('@angular/forms').FormControl<number>;
  status: import('@angular/forms').FormControl<LeaseStatusValue>;
  recurringCharges: FormArray<FormGroup<RecurringChargeControls>>;
}

/**
 * US-30: the slide-out Lease drawer on /properties/:id -- lists this property's leases and
 * lets a PM attach a resident to the unit with contract dates, base rent, a recurring
 * billing anchor day, and recurring sub-charges. Same shape/conventions as ResidentDrawer.
 * Only meaningful once a Property already exists, so PropertyFormContainer only renders/opens
 * this in edit mode, never on /properties/new.
 */
@Component({
  selector: 'app-lease-drawer',
  imports: [ReactiveFormsModule, FormsModule, TranslatePipe],
  templateUrl: './lease-drawer.html',
})
export class LeaseDrawer implements OnChanges {
  @Input({ required: true }) propertyId!: string;
  @Input() open = false;
  @Output() closed = new EventEmitter<void>();

  private readonly fb = inject(FormBuilder);
  private readonly leaseService = inject(LeaseService);
  private readonly residentService = inject(ResidentService);
  private readonly propertyService = inject(PropertyService);
  private readonly toastService = inject(ToastService);

  protected readonly leaseStatuses = Object.values(LeaseStatuses);
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

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['open'] && this.open) {
      this.loadLeases();
      this.loadResidents();
      this.loadPropertyMoveOutNotice();
    }
  }

  protected onMoveOutNoticeChange(value: string): void {
    this.savingMoveOutNotice.set(true);
    this.propertyService.updateMoveOutNotice(this.propertyId, { moveOutNoticeDate: value || null }).subscribe({
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
    this.propertyService.getProperty(this.propertyId).subscribe({
      next: (property) => this.propertyMoveOutNoticeDate.set(property.moveOutNoticeDate ?? null),
    });
  }

  protected close(): void {
    this.showForm.set(false);
    this.closed.emit();
  }

  protected startAdd(): void {
    this.form = this.buildForm();
    this.recomputeTotal();
    this.editingLeaseId.set(null);
    this.showForm.set(true);
  }

  protected startEdit(lease: LeaseResponse): void {
    this.form = this.buildForm();
    lease.recurringCharges.forEach(() => this.addRecurringChargeRow());
    this.form.patchValue({
      residentId: lease.residentId,
      startDate: lease.startDate.substring(0, 10),
      endDate: lease.endDate.substring(0, 10),
      monthlyBaseRent: lease.monthlyBaseRent,
      dueDayOfMonth: lease.dueDayOfMonth,
      status: lease.status,
    });
    lease.recurringCharges.forEach((charge, i) => {
      this.form.controls.recurringCharges.at(i).patchValue({
        chargeName: charge.chargeName,
        amount: charge.amount,
        accountingCode: charge.accountingCode,
      });
    });
    this.recomputeTotal();
    this.editingLeaseId.set(lease.id);
    this.showForm.set(true);
  }

  protected cancelForm(): void {
    this.showForm.set(false);
    this.editingLeaseId.set(null);
  }

  protected addRecurringChargeRow(): void {
    this.form.controls.recurringCharges.push(
      this.fb.nonNullable.group({
        chargeName: ['', Validators.required],
        amount: [0, [Validators.required, Validators.min(0)]],
        accountingCode: this.fb.control<string | null>(null),
      }),
    );
    this.recomputeTotal();
  }

  protected removeRecurringChargeRow(index: number): void {
    this.form.controls.recurringCharges.removeAt(index);
    this.recomputeTotal();
  }

  /** Called on (input) from monthlyBaseRent and every recurring charge amount field --
   * see liveTotalMonthlyDues's own comment for why this is a plain recompute rather than a
   * computed() over the form (the form itself is a mutable property, reassigned wholesale
   * by startAdd/startEdit, not a signal computed() could track). */
  protected recomputeTotal(): void {
    const raw = this.form.getRawValue();
    const addOns = raw.recurringCharges.reduce((sum, c) => sum + (Number(c.amount) || 0), 0);
    this.liveTotalMonthlyDues.set((Number(raw.monthlyBaseRent) || 0) + addOns);
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
      monthlyBaseRent: raw.monthlyBaseRent,
      dueDayOfMonth: raw.dueDayOfMonth,
      status: raw.status,
      recurringCharges: raw.recurringCharges.map((c) => ({
        chargeName: c.chargeName,
        amount: c.amount,
        accountingCode: c.accountingCode,
      })),
    };

    const leaseId = this.editingLeaseId();
    const call = leaseId
      ? this.leaseService.updateLease(this.propertyId, leaseId, request)
      : this.leaseService.createLease(this.propertyId, request);

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
    this.leaseService.deleteLease(this.propertyId, lease.id).subscribe({
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
    this.leaseService.createMoveInCharge(this.propertyId, lease.id, { moveInDate: this.moveInDate() }).subscribe({
      next: () => {
        this.creatingMoveInCharge.set(false);
        this.moveInChargeLeaseId.set(null);
        // Amount/description are visible in the Charges & Fines modal (US-31) -- the created
        // record IS a ManualCharge, no separate confirmation view needed.
        this.toastService.show('leases.drawer.moveInChargeCreatedToast');
      },
      error: () => {
        this.creatingMoveInCharge.set(false);
        this.toastService.show('leases.drawer.errorToast');
      },
    });
  }

  protected residentName(residentId: string): string {
    const resident = this.residents().find((r) => r.id === residentId);
    return resident ? `${resident.firstName} ${resident.lastName}` : residentId;
  }

  private loadLeases(): void {
    this.loading.set(true);
    this.leaseService.listLeases(this.propertyId).subscribe({
      next: (leases) => {
        this.leases.set(leases);
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

  private buildForm(): FormGroup<LeaseFormControls> {
    return this.fb.nonNullable.group({
      residentId: ['', Validators.required],
      startDate: ['', Validators.required],
      endDate: ['', Validators.required],
      monthlyBaseRent: [0, [Validators.required, Validators.min(0)]],
      dueDayOfMonth: [1, [Validators.required, Validators.min(1), Validators.max(28)]],
      status: ['FixedTerm' as LeaseStatusValue, Validators.required],
      recurringCharges: this.fb.array<FormGroup<RecurringChargeControls>>([]),
    });
  }
}
