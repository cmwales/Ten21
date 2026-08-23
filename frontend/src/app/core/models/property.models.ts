/** Mirrors Ten21.Domain.Enums.PropertyType. */
export const PropertyTypes = {
  SingleFamily: 'SingleFamily',
  MultiFamily: 'MultiFamily',
  Duplex: 'Duplex',
  Commercial: 'Commercial',
} as const;

export type PropertyTypeValue = (typeof PropertyTypes)[keyof typeof PropertyTypes];

/** Mirrors Ten21.Domain.Enums.OccupancyStatus. */
export const OccupancyStatuses = {
  Vacant: 'Vacant',
  Occupied: 'Occupied',
  Maintenance: 'Maintenance',
} as const;

export type OccupancyStatusValue = (typeof OccupancyStatuses)[keyof typeof OccupancyStatuses];

/** Mirrors Ten21.Api.Contracts.Properties.UnitRequest (US-19). Id is null for a brand-new
 * unit not yet persisted. */
export interface UnitRequest {
  id: string | null;
  unitIdentifier: string;
  targetRent: number | null;
  occupancyStatus: OccupancyStatusValue;
}

/** Mirrors Ten21.Api.Contracts.Properties.UpsertPropertyRequest (US-19). */
export interface UpsertPropertyRequest {
  name: string;
  propertyType: PropertyTypeValue;
  streetAddress1: string;
  streetAddress2: string | null;
  city: string;
  state: string;
  postalCode: string;
  country: string;
  defaultTargetRent: number | null;
  units: UnitRequest[];
}

/** Mirrors Ten21.Api.Contracts.Properties.UnitResponse. */
export interface UnitResponse {
  id: string;
  unitIdentifier: string;
  targetRent: number | null;
  occupancyStatus: OccupancyStatusValue;
}

/** Mirrors Ten21.Api.Contracts.Properties.PropertyResponse. */
export interface PropertyResponse {
  id: string;
  name: string;
  propertyType: PropertyTypeValue;
  streetAddress1: string;
  streetAddress2: string | null;
  city: string;
  state: string;
  postalCode: string;
  country: string;
  defaultTargetRent: number | null;
  units: UnitResponse[];
}

/** Mirrors Ten21.Api.Contracts.Properties.PropertyListUnitDto (US-20). */
export interface PropertyListUnitDto {
  id: string;
  unitIdentifier: string;
  occupancyStatus: OccupancyStatusValue;
  targetRent: number | null;
}

/** Mirrors Ten21.Api.Contracts.Properties.PropertyListItemDto (US-20). */
export interface PropertyListItemDto {
  id: string;
  name: string;
  propertyType: PropertyTypeValue;
  streetAddress1: string;
  city: string;
  state: string;
  postalCode: string;
  units: PropertyListUnitDto[];
}

/** Mirrors Ten21.Api.Contracts.Properties.PropertyListResponse (US-20). */
export interface PropertyListResponse {
  items: PropertyListItemDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}

/** Mirrors Ten21.Api.Contracts.Properties.ImportRowResult (US-21). */
export interface ImportRowResult {
  rowNumber: number;
  propertyName: string;
  propertyType: string;
  streetAddress1: string;
  city: string;
  state: string;
  postalCode: string;
  country: string;
  unitIdentifier: string;
  targetRent: string;
  isValid: boolean;
  errors: string[];
}

/** Mirrors Ten21.Api.Contracts.Properties.ImportPropertiesResponse (US-21). */
export interface ImportPropertiesResponse {
  success: boolean;
  totalRows: number;
  invalidRowCount: number;
  propertiesCreated: number;
  unitsCreated: number;
  rows: ImportRowResult[];
}
