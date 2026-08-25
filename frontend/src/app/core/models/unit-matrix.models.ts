/** Mirrors Ten21.Api.Contracts.UnitTiers.UpsertUnitTierRequest / UnitTierResponse. */
export interface UpsertUnitTierRequest {
  tierName: string;
  defaultRent: number;
  accountingCode: string | null;
  description: string | null;
}

export interface UnitTierResponse {
  id: string;
  tierName: string;
  defaultRent: number;
  accountingCode: string | null;
  description: string | null;
}

/** Mirrors Ten21.Api.Contracts.UnitGroups.UpsertUnitGroupRequest / UnitGroupResponse. */
export interface UpsertUnitGroupRequest {
  groupName: string;
  description: string | null;
}

export interface UnitGroupResponse {
  id: string;
  groupName: string;
  description: string | null;
}

/** Mirrors Ten21.Api.Contracts.Properties.UpdatePropertyMatrixRowRequest /
 * PropertyMatrixRowResponse. */
export interface UpdatePropertyMatrixRowRequest {
  unitGroupId: string | null;
  unitTierId: string | null;
  targetRent: number | null;
}

export interface PropertyMatrixRowResponse {
  id: string;
  unitIdentifier: string | null;
  unitGroupId: string | null;
  unitTierId: string | null;
  targetRent: number | null;
}

/** Mirrors Ten21.Domain.Enums.MatrixBatchField. */
export const MatrixBatchFields = {
  UnitGroup: 'UnitGroup',
  UnitTier: 'UnitTier',
} as const;

export type MatrixBatchFieldValue = (typeof MatrixBatchFields)[keyof typeof MatrixBatchFields];

/** Mirrors Ten21.Api.Contracts.Properties.BatchAssignMatrixRequest. */
export interface BatchAssignMatrixRequest {
  propertyIds: string[];
  field: MatrixBatchFieldValue;
  valueId: string | null;
}
