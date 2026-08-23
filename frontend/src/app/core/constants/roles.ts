/**
 * Mirrors Ten21.Domain.Common.RoleNames (SECURITY.md §4.1's 9-tier role taxonomy).
 * Kept as plain string constants for the same reason as the backend original: role
 * names are DB rows, not a closed enum.
 */
export const RoleNames = {
  SuperAdmin: 'SuperAdmin',
  PropertyManager: 'PropertyManager',
  BoardMember: 'BoardMember',
  PropertyOwner: 'PropertyOwner',
  Tenant: 'Tenant',
  Vendor: 'Vendor',
  CommitteeMember: 'CommitteeMember',
  OnSiteStaff: 'OnSiteStaff',
  Accountant: 'Accountant',
} as const;

export type RoleName = (typeof RoleNames)[keyof typeof RoleNames];
