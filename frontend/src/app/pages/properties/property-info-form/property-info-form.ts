import { Component, Input } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { PropertyTypes } from '../../../core/models/property.models';
import { PropertyFormGroup } from '../property-form.types';

/** US-19: the property-level half of PropertyFormContainer's unified form. Receives the
 * parent's FormGroup directly (bound via [formGroup] on its own root element) rather than
 * a ControlContainer/formGroupName setup -- simpler for a form that's never reused outside
 * this one container. */
@Component({
  selector: 'app-property-info-form',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './property-info-form.html',
})
export class PropertyInfoForm {
  @Input({ required: true }) form!: PropertyFormGroup;

  protected readonly propertyTypes = Object.values(PropertyTypes);
}
