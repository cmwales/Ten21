import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { ComponentWithUnsavedChanges } from '../../../core/guards/unsaved-changes.guard';
import { OccupancyStatusValue, PropertyResponse, PropertyTypeValue } from '../../../core/models/property.models';
import { PropertyService } from '../../../core/services/property.service';
import { ToastService } from '../../../core/services/toast.service';
import { PropertyInfoForm } from '../property-info-form/property-info-form';
import { PropertyFormGroup } from '../property-form.types';

/**
 * PropertyFormContainer -- one flat component serving both /properties/new (create mode)
 * and /properties/:id (edit mode). Flat: no nested/embedded unit section (see Property's own
 * class comment for why -- there is no separate Unit entity). Save/Apply/Cancel toolbar;
 * Apply keeps the user on this page and converts the route to /properties/:id in place
 * (replaceUrl, no new history entry) rather than navigating away, matching "retains user on
 * page."
 */
@Component({
  selector: 'app-property-form-container',
  imports: [ReactiveFormsModule, TranslatePipe, PropertyInfoForm],
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
    unitIdentifier: this.fb.control<string | null>(null, Validators.maxLength(50)),
    targetRent: this.fb.control<number | null>(null),
    occupancyStatus: ['Vacant' as OccupancyStatusValue, Validators.required],
  });

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

    const raw = this.form.getRawValue();
    const request = {
      name: raw.name,
      propertyType: raw.propertyType,
      streetAddress1: raw.streetAddress1,
      streetAddress2: raw.streetAddress2 || null,
      city: raw.city,
      state: raw.state,
      postalCode: raw.postalCode,
      country: raw.country,
      unitIdentifier: raw.unitIdentifier,
      targetRent: raw.targetRent,
      occupancyStatus: raw.occupancyStatus,
    };
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
          unitIdentifier: property.unitIdentifier,
          targetRent: property.targetRent,
          occupancyStatus: property.occupancyStatus,
        });
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

    if (error.status === 409) {
      return 'properties.form.duplicateError';
    }

    return 'properties.form.networkError';
  }
}
