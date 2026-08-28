import { Component, inject, input, signal } from '@angular/core';
import { FormArray, FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { OccupantTypeValue, OccupantTypes, ResidentResponse, UpsertResidentRequest } from '../../../core/models/resident.models';
import { ResidentService } from '../../../core/services/resident.service';
import { ModalBase } from '../../../shared/modal-base';
import { ToastService } from '../../../core/services/toast.service';

interface EmergencyContactControls {
  name: import('@angular/forms').FormControl<string>;
  phoneNumber: import('@angular/forms').FormControl<string>;
  relationship: import('@angular/forms').FormControl<string | null>;
}

interface ResidentFormControls {
  occupantType: import('@angular/forms').FormControl<OccupantTypeValue>;
  firstName: import('@angular/forms').FormControl<string>;
  lastName: import('@angular/forms').FormControl<string>;
  email: import('@angular/forms').FormControl<string | null>;
  phoneNumber: import('@angular/forms').FormControl<string | null>;
  noticeGivenDate: import('@angular/forms').FormControl<string | null>;
  forwardingAddress: import('@angular/forms').FormControl<string | null>;
  showInDirectory: import('@angular/forms').FormControl<boolean>;
  emergencyContacts: FormArray<FormGroup<EmergencyContactControls>>;
}

/**
 * US-23: the slide-out Tenant Quick-View Drawer on /properties/:id -- lists this property's
 * residents and lets a PM add/edit/delete them, including a dynamic list of emergency
 * contacts per resident. Only meaningful once a Property already exists (propertyId is
 * required), so PropertyFormContainer only renders/opens this in edit mode, never on
 * /properties/new.
 */
@Component({
  selector: 'app-resident-drawer',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './resident-drawer.html',
})
export class ResidentDrawer extends ModalBase {
  readonly propertyId = input.required<string>();

  private readonly fb = inject(FormBuilder);
  private readonly residentService = inject(ResidentService);
  private readonly toastService = inject(ToastService);

  protected readonly occupantTypes = Object.values(OccupantTypes);
  protected readonly residents = signal<ResidentResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly submitting = signal(false);
  protected readonly showForm = signal(false);
  protected readonly editingResidentId = signal<string | null>(null);

  protected form: FormGroup<ResidentFormControls> = this.buildForm();

  protected override onOpen(): void {
    this.loadResidents();
  }

  protected override close(): void {
    this.showForm.set(false);
    super.close();
  }

  protected startAdd(): void {
    this.form = this.buildForm();
    this.editingResidentId.set(null);
    this.showForm.set(true);
  }

  protected startEdit(resident: ResidentResponse): void {
    this.form = this.buildForm();
    resident.emergencyContacts.forEach(() => this.addEmergencyContactRow());
    this.form.patchValue({
      occupantType: resident.occupantType,
      firstName: resident.firstName,
      lastName: resident.lastName,
      email: resident.email,
      phoneNumber: resident.phoneNumber,
      noticeGivenDate: resident.noticeGivenDate ? resident.noticeGivenDate.substring(0, 10) : null,
      forwardingAddress: resident.forwardingAddress,
      showInDirectory: resident.showInDirectory,
    });
    resident.emergencyContacts.forEach((contact, i) => {
      this.form.controls.emergencyContacts.at(i).patchValue({
        name: contact.name,
        phoneNumber: contact.phoneNumber,
        relationship: contact.relationship,
      });
    });
    this.editingResidentId.set(resident.id);
    this.showForm.set(true);
  }

  protected cancelForm(): void {
    this.showForm.set(false);
    this.editingResidentId.set(null);
  }

  protected addEmergencyContactRow(): void {
    this.form.controls.emergencyContacts.push(
      this.fb.nonNullable.group({
        name: ['', Validators.required],
        phoneNumber: ['', Validators.required],
        relationship: this.fb.control<string | null>(null),
      }),
    );
  }

  protected removeEmergencyContactRow(index: number): void {
    this.form.controls.emergencyContacts.removeAt(index);
  }

  protected save(): void {
    if (this.submitting() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    const raw = this.form.getRawValue();
    const request: UpsertResidentRequest = {
      occupantType: raw.occupantType,
      firstName: raw.firstName,
      lastName: raw.lastName,
      email: raw.email,
      phoneNumber: raw.phoneNumber,
      forwardingAddress: raw.forwardingAddress,
      noticeGivenDate: raw.noticeGivenDate,
      showInDirectory: raw.showInDirectory,
      emergencyContacts: raw.emergencyContacts.map((c) => ({
        name: c.name,
        phoneNumber: c.phoneNumber,
        relationship: c.relationship,
      })),
    };

    const residentId = this.editingResidentId();
    const call = residentId
      ? this.residentService.updateResident(this.propertyId(), residentId, request)
      : this.residentService.createResident(this.propertyId(), request);

    call.subscribe({
      next: () => {
        this.submitting.set(false);
        this.showForm.set(false);
        this.editingResidentId.set(null);
        this.toastService.show(residentId ? 'residents.drawer.savedToast' : 'residents.drawer.addedToast');
        this.loadResidents();
      },
      error: () => {
        this.submitting.set(false);
        this.toastService.show('residents.drawer.errorToast');
      },
    });
  }

  protected deleteResident(resident: ResidentResponse): void {
    this.residentService.deleteResident(this.propertyId(), resident.id).subscribe({
      next: () => {
        this.toastService.show('residents.drawer.removedToast');
        this.loadResidents();
      },
      error: () => this.toastService.show('residents.drawer.errorToast'),
    });
  }

  private loadResidents(): void {
    this.loading.set(true);
    this.residentService.listResidents(this.propertyId()).subscribe({
      next: (residents) => {
        this.residents.set(residents);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  private buildForm(): FormGroup<ResidentFormControls> {
    return this.fb.nonNullable.group({
      occupantType: ['Primary' as OccupantTypeValue, Validators.required],
      firstName: ['', Validators.required],
      lastName: ['', Validators.required],
      email: this.fb.control<string | null>(null),
      phoneNumber: this.fb.control<string | null>(null),
      noticeGivenDate: this.fb.control<string | null>(null),
      forwardingAddress: this.fb.control<string | null>(null),
      showInDirectory: [false],
      emergencyContacts: this.fb.array<FormGroup<EmergencyContactControls>>([]),
    });
  }
}
