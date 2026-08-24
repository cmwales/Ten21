import { Component, Input } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { OccupancyStatuses, PropertyTypes } from '../../../core/models/property.models';
import { FormField } from '../../../shared/form-field/form-field';
import { PropertyFormGroup } from '../property-form.types';

/** The Property form -- flat, no nested/embedded sections. Every field here (including
 * Unit/Suite #, Target Rent, and Occupancy Status) lives directly on Property; there is no
 * separate child Unit entity or form section. See Property's own class comment for the
 * history: an earlier design embedded a Units sub-form here, reversed after tester feedback
 * ("Each suite in a building needs to be a new property. They need to be setup
 * independently"). */
@Component({
  selector: 'app-property-info-form',
  imports: [ReactiveFormsModule, TranslatePipe, FormField],
  templateUrl: './property-info-form.html',
})
export class PropertyInfoForm {
  @Input({ required: true }) form!: PropertyFormGroup;

  protected readonly propertyTypes = Object.values(PropertyTypes);
  protected readonly occupancyStatuses = Object.values(OccupancyStatuses);
}
