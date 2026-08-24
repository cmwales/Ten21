/** Mirrors Ten21.Domain.Enums.OccupantType. */
export const OccupantTypes = {
  Primary: 'Primary',
  Secondary: 'Secondary',
} as const;

export type OccupantTypeValue = (typeof OccupantTypes)[keyof typeof OccupantTypes];

/** Mirrors Ten21.Api.Contracts.Residents.EmergencyContactRequest. */
export interface EmergencyContactRequest {
  name: string;
  phoneNumber: string;
  relationship: string | null;
}

/** Mirrors Ten21.Api.Contracts.Residents.EmergencyContactResponse. */
export interface EmergencyContactResponse {
  id: string;
  name: string;
  phoneNumber: string;
  relationship: string | null;
}

/** Mirrors Ten21.Api.Contracts.Residents.UpsertResidentRequest. */
export interface UpsertResidentRequest {
  occupantType: OccupantTypeValue;
  firstName: string;
  lastName: string;
  email: string | null;
  phoneNumber: string | null;
  forwardingAddress: string | null;
  noticeGivenDate: string | null;
  showInDirectory: boolean;
  emergencyContacts: EmergencyContactRequest[];
}

/** Mirrors Ten21.Api.Contracts.Residents.ResidentResponse. */
export interface ResidentResponse {
  id: string;
  propertyId: string;
  userId: string | null;
  occupantType: OccupantTypeValue;
  firstName: string;
  lastName: string;
  email: string | null;
  phoneNumber: string | null;
  forwardingAddress: string | null;
  noticeGivenDate: string | null;
  showInDirectory: boolean;
  emergencyContacts: EmergencyContactResponse[];
}
