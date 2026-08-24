import { FormControl, FormGroup } from '@angular/forms';
import { OccupancyStatusValue, PropertyTypeValue } from '../../core/models/property.models';

/** An explicit typed-reactive-forms shape for PropertyFormContainer/PropertyInfoForm -- a
 * bare `FormGroup` type on the @Input() would fall back to an index-signature-typed
 * `controls` object, which the project's strict TS config (noPropertyAccessFromIndexSignature)
 * then rejects for every `form.controls.name`-style access in the templates. Flat -- no
 * nested unit array (Property has no child entities; see Property's own class comment). */
export interface PropertyFormControls {
  name: FormControl<string>;
  propertyType: FormControl<PropertyTypeValue>;
  streetAddress1: FormControl<string>;
  streetAddress2: FormControl<string>;
  city: FormControl<string>;
  state: FormControl<string>;
  postalCode: FormControl<string>;
  country: FormControl<string>;
  unitIdentifier: FormControl<string>;
  targetRent: FormControl<number | null>;
  occupancyStatus: FormControl<OccupancyStatusValue>;
}
export type PropertyFormGroup = FormGroup<PropertyFormControls>;
