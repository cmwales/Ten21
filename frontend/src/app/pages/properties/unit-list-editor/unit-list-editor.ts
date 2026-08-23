import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormArray, ReactiveFormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { OccupancyStatuses } from '../../../core/models/property.models';
import { UnitFormGroup } from '../property-form.types';

/** US-19: the per-unit half of PropertyFormContainer's unified form. Deliberately "dumb" --
 * renders the FormArray it's given and emits add/remove intents rather than owning the
 * FormArray mutation itself, so the one place that knows how to build a new unit FormGroup
 * (including the DefaultTargetRent cascade) is PropertyFormContainer, not duplicated here. */
@Component({
  selector: 'app-unit-list-editor',
  imports: [ReactiveFormsModule, TranslatePipe],
  templateUrl: './unit-list-editor.html',
})
export class UnitListEditor {
  @Input({ required: true }) unitsArray!: FormArray<UnitFormGroup>;
  @Output() readonly addUnit = new EventEmitter<void>();
  @Output() readonly removeUnit = new EventEmitter<number>();

  protected readonly occupancyStatuses = Object.values(OccupancyStatuses);
}
