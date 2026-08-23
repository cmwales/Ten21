import { FormArray, FormControl, FormGroup } from '@angular/forms';
import { OccupancyStatusValue, PropertyTypeValue } from '../../core/models/property.models';

/** Explicit typed-reactive-forms shapes shared between PropertyFormContainer and its two
 * sub-components -- a plain `FormGroup`/`FormArray` type on the @Input()s would fall back to
 * an index-signature-typed `controls` object, which the project's strict TS config
 * (noPropertyAccessFromIndexSignature) then rejects for every `form.controls.name`-style
 * access in the templates. */
export interface UnitFormControls {
  id: FormControl<string | null>;
  unitIdentifier: FormControl<string>;
  targetRent: FormControl<number | null>;
  occupancyStatus: FormControl<OccupancyStatusValue>;
}
export type UnitFormGroup = FormGroup<UnitFormControls>;

export interface PropertyFormControls {
  name: FormControl<string>;
  propertyType: FormControl<PropertyTypeValue>;
  streetAddress1: FormControl<string>;
  streetAddress2: FormControl<string>;
  city: FormControl<string>;
  state: FormControl<string>;
  postalCode: FormControl<string>;
  country: FormControl<string>;
  defaultTargetRent: FormControl<number | null>;
  units: FormArray<UnitFormGroup>;
}
export type PropertyFormGroup = FormGroup<PropertyFormControls>;
