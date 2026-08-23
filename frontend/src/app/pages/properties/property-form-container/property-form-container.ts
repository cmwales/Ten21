import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { ComponentWithUnsavedChanges } from '../../../core/guards/unsaved-changes.guard';
import {
  OccupancyStatusValue,
  PropertyResponse,
  PropertyTypeValue,
  UnitRequest,
  UnitResponse,
  UpsertPropertyRequest,
} from '../../../core/models/property.models';
import { PropertyService } from '../../../core/services/property.service';
import { ToastService } from '../../../core/services/toast.service';
import { PropertyInfoForm } from '../property-info-form/property-info-form';
import { PropertyFormGroup, UnitFormGroup } from '../property-form.types';
import { UnitListEditor } from '../unit-list-editor/unit-list-editor';

/**
 * US-19: PropertyFormContainer -- one component serving both /properties/new (create mode)
 * and /properties/:id (edit mode), per the acceptance criteria's "single unified form"
 * requirement. Save/Apply/Cancel toolbar; Apply keeps the user on this page and converts
 * the route to /properties/:id in place (replaceUrl, no new history entry) rather than
 * navigating away, matching "retains user on page."
 */
@Component({
  selector: 'app-property-form-container',
  imports: [ReactiveFormsModule, TranslatePipe, PropertyInfoForm, UnitListEditor],
  templateUrl: './property-form-container.html',
})
export class PropertyFormContainer implements OnInit, ComponentWithUnsavedChanges {
  private readonly fb = inject(FormBuilder);
  private readonly propertyService = inject(PropertyService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toastService = inject(ToastService);

  protected readonly submitting = signal(false);
  protected readonly loading = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly propertyId = signal<string | null>(null);

  protected readonly form: PropertyFormGroup = this.fb.nonNullable.group({
    name: ['', Validators.required],
    propertyType: ['SingleFamily' as PropertyTypeValue, Validators.required],
    streetAddress1: ['', Validators.required],
    streetAddress2: [''],
    city: ['', Validators.required],
    state: ['', Validators.required],
    postalCode: ['', Validators.required],
    country: ['USA', Validators.required],
    defaultTargetRent: this.fb.control<number | null>(null),
    units: this.fb.array<UnitFormGroup>([]),
  });

  protected get unitsArray(): FormArray<UnitFormGroup> {
    return this.form.controls.units;
  }

  ngOnInit(): void {
    const idParam = this.route.snapshot.paramMap.get('id');
    if (idParam) {
      this.propertyId.set(idParam);
      this.loadProperty(idParam);
    }
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected addUnit(): void {
    this.unitsArray.push(this.buildUnitGroup());
    this.form.markAsDirty();
  }

  protected removeUnit(index: number): void {
    this.unitsArray.removeAt(index);
    this.form.markAsDirty();
  }

  protected save(): void {
    this.submit((property) => {
      this.toastService.show('properties.form.savedToast');
      void this.router.navigateByUrl('/properties');
    });
  }

  protected apply(): void {
    this.submit((property) => {
      this.propertyId.set(property.id);
      void this.router.navigate(['/properties', property.id], { replaceUrl: true });
    });
  }

  protected cancel(): void {
    void this.router.navigateByUrl('/properties');
  }

  private submit(onSuccess: (property: PropertyResponse) => void): void {
    if (this.submitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.errorKey.set(null);

    const request = this.buildRequest();
    const id = this.propertyId();
    const call = id ? this.propertyService.updateProperty(id, request) : this.propertyService.createProperty(request);

    call.subscribe({
      next: (property) => {
        this.submitting.set(false);
        this.form.markAsPristine();
        onSuccess(property);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorKey.set(this.resolveErrorKey(error));
      },
    });
  }

  private buildRequest(): UpsertPropertyRequest {
    const raw = this.form.getRawValue();
    return {
      name: raw.name,
      propertyType: raw.propertyType,
      streetAddress1: raw.streetAddress1,
      streetAddress2: raw.streetAddress2 || null,
      city: raw.city,
      state: raw.state,
      postalCode: raw.postalCode,
      country: raw.country,
      defaultTargetRent: raw.defaultTargetRent,
      units: raw.units.map(
        (unit): UnitRequest => ({
          id: unit.id,
          unitIdentifier: unit.unitIdentifier,
          targetRent: unit.targetRent,
          occupancyStatus: unit.occupancyStatus,
        }),
      ),
    };
  }

  private buildUnitGroup(unit?: UnitResponse): UnitFormGroup {
    return this.fb.nonNullable.group({
      id: this.fb.control<string | null>(unit?.id ?? null),
      unitIdentifier: [unit?.unitIdentifier ?? '', Validators.required],
      targetRent: this.fb.control<number | null>(unit?.targetRent ?? null),
      occupancyStatus: [(unit?.occupancyStatus ?? 'Vacant') as OccupancyStatusValue, Validators.required],
    });
  }

  private loadProperty(id: string): void {
    this.loading.set(true);
    this.propertyService.getProperty(id).subscribe({
      next: (property) => {
        this.loading.set(false);
        this.form.patchValue({
          name: property.name,
          propertyType: property.propertyType,
          streetAddress1: property.streetAddress1,
          streetAddress2: property.streetAddress2 ?? '',
          city: property.city,
          state: property.state,
          postalCode: property.postalCode,
          country: property.country,
          defaultTargetRent: property.defaultTargetRent,
        });
        this.unitsArray.clear();
        for (const unit of property.units) {
          this.unitsArray.push(this.buildUnitGroup(unit));
        }
        this.form.markAsPristine();
      },
      error: () => {
        this.loading.set(false);
        this.errorKey.set('properties.form.loadError');
      },
    });
  }

  private resolveErrorKey(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'properties.form.networkError';
    }

    if (error.status === 400) {
      return 'properties.form.validationError';
    }

    if (error.status === 404) {
      return 'properties.form.notFoundError';
    }

    return 'properties.form.networkError';
  }
}
