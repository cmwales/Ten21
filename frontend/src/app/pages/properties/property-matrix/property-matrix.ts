import { Component, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { PropertyListItemDto } from '../../../core/models/property.models';
import { MatrixBatchFields, MatrixBatchFieldValue, UnitGroupResponse, UnitTierResponse } from '../../../core/models/unit-matrix.models';
import { PropertyService } from '../../../core/services/property.service';
import { ToastService } from '../../../core/services/toast.service';
import { UnitMatrixService } from '../../../core/services/unit-matrix.service';
import { AppHeader } from '../../../shared/app-header/app-header';

interface MatrixRow {
  id: string;
  unitIdentifier: string | null;
  name: string;
  unitGroupId: string | null;
  unitTierId: string | null;
  targetRent: number | null;
}

const SAVE_DEBOUNCE_MS = 500;
const SAVED_INDICATOR_MS = 2000;

/**
 * US-29: portfolio-wide spreadsheet-style editor -- every Property row (door/suite) the
 * workspace owns, in one grid, so a PM can bulk-assign UnitGroup/UnitTier instead of editing
 * doors one at a time on /properties/:id. Deliberately its own page (not nested under
 * /properties/:id, unlike the spec's original wording) -- Property was flattened in Sprint 3
 * so :id already IS a single door; there's no parent "property" left to host a multi-row
 * matrix under. Purely a property-configuration step: no resident, lease, or charge is
 * touched here.
 */
@Component({
  selector: 'app-property-matrix',
  imports: [ReactiveFormsModule, FormsModule, TranslatePipe, AppHeader],
  templateUrl: './property-matrix.html',
})
export class PropertyMatrix implements OnInit {
  private readonly propertyService = inject(PropertyService);
  private readonly unitMatrixService = inject(UnitMatrixService);
  private readonly toastService = inject(ToastService);
  private readonly fb = inject(FormBuilder);
  private readonly elementRef: ElementRef<HTMLElement> = inject(ElementRef);

  protected readonly matrixFields = MatrixBatchFields;

  protected readonly loading = signal(true);
  protected readonly rows = signal<MatrixRow[]>([]);
  protected readonly tiers = signal<UnitTierResponse[]>([]);
  protected readonly groups = signal<UnitGroupResponse[]>([]);

  protected readonly selectedIds = signal<ReadonlySet<string>>(new Set());
  protected readonly allSelected = computed(() => this.rows().length > 0 && this.selectedIds().size === this.rows().length);
  protected readonly saveStateByRow = signal<ReadonlyMap<string, 'saving' | 'saved'>>(new Map());

  protected readonly batchField = signal<MatrixBatchFieldValue>(MatrixBatchFields.UnitGroup);
  protected readonly batchValueId = signal<string>('');
  protected readonly batchApplying = signal(false);

  protected readonly tierForm = this.fb.nonNullable.group({
    tierName: ['', Validators.required],
    defaultRent: [0, [Validators.required, Validators.min(0)]],
    accountingCode: this.fb.control<string | null>(null),
    description: this.fb.control<string | null>(null),
  });

  protected readonly groupForm = this.fb.nonNullable.group({
    groupName: ['', Validators.required],
    description: this.fb.control<string | null>(null),
  });

  protected readonly tierSubmitting = signal(false);
  protected readonly groupSubmitting = signal(false);

  private readonly saveTimers = new Map<string, ReturnType<typeof setTimeout>>();

  ngOnInit(): void {
    this.loadAll();
  }

  private loadAll(): void {
    this.loading.set(true);
    forkJoin({
      properties: this.propertyService.listProperties(),
      tiers: this.unitMatrixService.listUnitTiers(),
      groups: this.unitMatrixService.listUnitGroups(),
    }).subscribe({
      next: ({ properties, tiers, groups }) => {
        this.rows.set(properties.items.map((p: PropertyListItemDto) => ({
          id: p.id,
          unitIdentifier: p.unitIdentifier,
          name: p.name,
          unitGroupId: p.unitGroupId ?? null,
          unitTierId: p.unitTierId ?? null,
          targetRent: p.targetRent,
        })));
        this.tiers.set(tiers);
        this.groups.set(groups);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toastService.show('properties.matrix.loadError');
      },
    });
  }

  protected tierName(id: string | null): string | null {
    return id === null ? null : (this.tiers().find((t) => t.id === id)?.tierName ?? null);
  }

  protected groupName(id: string | null): string | null {
    return id === null ? null : (this.groups().find((g) => g.id === id)?.groupName ?? null);
  }

  protected onGroupChange(row: MatrixRow, value: string): void {
    this.updateRow(row.id, { unitGroupId: value || null });
  }

  /** Tier selection prefills TargetRent from the tier's DefaultRent -- a starting value the
   * manager can still overwrite afterward without it snapping back (only re-selecting the
   * tier re-prefills it). */
  protected onTierChange(row: MatrixRow, value: string): void {
    const tierId = value || null;
    const tier = tierId === null ? null : this.tiers().find((t) => t.id === tierId);
    this.updateRow(row.id, { unitTierId: tierId, targetRent: tier ? tier.defaultRent : row.targetRent });
  }

  protected onTargetRentChange(row: MatrixRow, value: string): void {
    const parsed = value.trim() === '' ? null : Number(value);
    this.updateRow(row.id, { targetRent: Number.isFinite(parsed) ? parsed : null });
  }

  private updateRow(id: string, patch: Partial<MatrixRow>): void {
    let updated: MatrixRow | undefined;
    this.rows.update((current) =>
      current.map((row) => {
        if (row.id !== id) {
          return row;
        }
        updated = { ...row, ...patch };
        return updated;
      }),
    );
    if (updated) {
      this.scheduleSave(updated);
    }
  }

  private scheduleSave(row: MatrixRow): void {
    const existingTimer = this.saveTimers.get(row.id);
    if (existingTimer) {
      clearTimeout(existingTimer);
    }

    this.saveTimers.set(
      row.id,
      setTimeout(() => this.saveRow(row), SAVE_DEBOUNCE_MS),
    );
  }

  private saveRow(row: MatrixRow): void {
    this.saveTimers.delete(row.id);
    this.setSaveState(row.id, 'saving');

    this.unitMatrixService
      .updateMatrixRow(row.id, {
        unitGroupId: row.unitGroupId,
        unitTierId: row.unitTierId,
        targetRent: row.targetRent,
      })
      .subscribe({
        next: () => {
          this.setSaveState(row.id, 'saved');
          setTimeout(() => this.clearSaveState(row.id), SAVED_INDICATOR_MS);
        },
        error: () => {
          this.clearSaveState(row.id);
          this.toastService.show('properties.matrix.saveError');
        },
      });
  }

  private setSaveState(id: string, state: 'saving' | 'saved'): void {
    this.saveStateByRow.update((current) => new Map(current).set(id, state));
  }

  private clearSaveState(id: string): void {
    this.saveStateByRow.update((current) => {
      const next = new Map(current);
      next.delete(id);
      return next;
    });
  }

  protected toggleSelectAll(): void {
    this.selectedIds.set(this.allSelected() ? new Set() : new Set(this.rows().map((r) => r.id)));
  }

  protected toggleSelectRow(id: string): void {
    this.selectedIds.update((current) => {
      const next = new Set(current);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  protected applyBatch(): void {
    const propertyIds = [...this.selectedIds()];
    if (propertyIds.length === 0 || this.batchApplying()) {
      return;
    }

    const valueId = this.batchValueId() || null;
    this.batchApplying.set(true);

    this.unitMatrixService
      .batchAssignMatrix({ propertyIds, field: this.batchField(), valueId })
      .subscribe({
        next: (updatedRows) => {
          const byId = new Map(updatedRows.map((r) => [r.id, r]));
          this.rows.update((current) =>
            current.map((row) => {
              const updated = byId.get(row.id);
              return updated
                ? { ...row, unitGroupId: updated.unitGroupId, unitTierId: updated.unitTierId, targetRent: updated.targetRent }
                : row;
            }),
          );
          this.batchApplying.set(false);
          this.selectedIds.set(new Set());
          this.toastService.show('properties.matrix.batchAppliedToast');
        },
        error: () => {
          this.batchApplying.set(false);
          this.toastService.show('properties.matrix.saveError');
        },
      });
  }

  protected saveTier(): void {
    if (this.tierSubmitting() || this.tierForm.invalid) {
      this.tierForm.markAllAsTouched();
      return;
    }

    this.tierSubmitting.set(true);
    const raw = this.tierForm.getRawValue();
    this.unitMatrixService
      .createUnitTier({
        tierName: raw.tierName,
        defaultRent: raw.defaultRent,
        accountingCode: raw.accountingCode,
        description: raw.description,
      })
      .subscribe({
        next: (tier) => {
          this.tiers.update((current) => [...current, tier].sort((a, b) => a.tierName.localeCompare(b.tierName)));
          this.tierForm.reset({ tierName: '', defaultRent: 0, accountingCode: null, description: null });
          this.tierSubmitting.set(false);
        },
        error: () => {
          this.tierSubmitting.set(false);
          this.toastService.show('properties.matrix.saveError');
        },
      });
  }

  protected deleteTier(tier: UnitTierResponse): void {
    this.unitMatrixService.deleteUnitTier(tier.id).subscribe({
      next: () => this.tiers.update((current) => current.filter((t) => t.id !== tier.id)),
      error: () => this.toastService.show('properties.matrix.tierInUseError'),
    });
  }

  protected saveGroup(): void {
    if (this.groupSubmitting() || this.groupForm.invalid) {
      this.groupForm.markAllAsTouched();
      return;
    }

    this.groupSubmitting.set(true);
    const raw = this.groupForm.getRawValue();
    this.unitMatrixService
      .createUnitGroup({ groupName: raw.groupName, description: raw.description })
      .subscribe({
        next: (group) => {
          this.groups.update((current) => [...current, group].sort((a, b) => a.groupName.localeCompare(b.groupName)));
          this.groupForm.reset({ groupName: '', description: null });
          this.groupSubmitting.set(false);
        },
        error: () => {
          this.groupSubmitting.set(false);
          this.toastService.show('properties.matrix.saveError');
        },
      });
  }

  protected deleteGroup(group: UnitGroupResponse): void {
    this.unitMatrixService.deleteUnitGroup(group.id).subscribe({
      next: () => this.groups.update((current) => current.filter((g) => g.id !== group.id)),
      error: () => this.toastService.show('properties.matrix.groupInUseError'),
    });
  }

  /** Enter moves focus down to the same column on the next row -- Tab/Shift+Tab need no
   * handling of their own, the browser's natural DOM tab order already moves right/left
   * across a table's cells. */
  protected onCellKeydown(event: KeyboardEvent, rowIndex: number, column: 'group' | 'tier' | 'rent'): void {
    if (event.key !== 'Enter') {
      return;
    }
    event.preventDefault();
    const next = this.elementRef.nativeElement.querySelector<HTMLElement>(
      `[data-matrix-cell="${rowIndex + 1}-${column}"]`,
    );
    next?.focus();
  }
}
