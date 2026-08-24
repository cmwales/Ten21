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

/**
 * A single flat, standalone leasable space -- a whole single-family house, or one suite
 * within a larger building. There is deliberately no separate parent/child Unit concept:
 * Suite A and Suite B of the same building are two independent Property records that
 * happen to share a street address, distinguished by unitIdentifier. Mirrors
 * Ten21.Api.Contracts.Properties.UpsertPropertyRequest.
 */
export interface UpsertPropertyRequest {
  name: string;
  propertyType: PropertyTypeValue;
  streetAddress1: string;
  streetAddress2: string | null;
  city: string;
  state: string;
  postalCode: string;
  country: string;
  unitIdentifier: string | null;
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
  unitIdentifier: string | null;
  targetRent: number | null;
  occupancyStatus: OccupancyStatusValue;
}

/** Mirrors Ten21.Api.Contracts.Properties.PropertyListItemDto (US-20) -- one flat row per
 * property/suite. */
export interface PropertyListItemDto {
  id: string;
  name: string;
  propertyType: PropertyTypeValue;
  streetAddress1: string;
  city: string;
  state: string;
  postalCode: string;
  unitIdentifier: string | null;
  targetRent: number | null;
  occupancyStatus: OccupancyStatusValue;
}

/** Mirrors Ten21.Api.Contracts.Properties.PropertyListResponse (US-20). */
export interface PropertyListResponse {
  items: PropertyListItemDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}

/** Mirrors Ten21.Api.Contracts.Properties.ImportRowResult (US-21). One row = one flat
 * property (no more grouping rows into a parent with child units). */
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
  rows: ImportRowResult[];
}
