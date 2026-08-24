import { Component, Input } from '@angular/core';
import { AbstractControl } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';

/**
 * Wraps the label + error/hint markup every reactive-form field in this codebase repeated
 * verbatim (property-info-form.html alone had it nine times). Deliberately does NOT wrap
 * the actual <input>/<select> itself -- that would need a full ControlValueAccessor
 * implementation to stay generic across text/number/select controls, which is more
 * abstraction than this actually needs; the control stays inline via <ng-content>, owned by
 * the parent template, while only the label/error/hint chrome around it is shared.
 */
@Component({
  selector: 'app-form-field',
  imports: [TranslatePipe],
  templateUrl: './form-field.html',
})
export class FormField {
  @Input({ required: true }) controlId!: string;
  @Input({ required: true }) labelKey!: string;
  @Input({ required: true }) control!: AbstractControl;
  @Input() errorKey?: string;
  @Input() hintKey?: string;
}
