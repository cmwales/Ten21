import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { denyRolesGuard } from './core/guards/role.guard';
import { unsavedChangesGuard } from './core/guards/unsaved-changes.guard';
import { RoleNames } from './core/constants/roles';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./pages/login/login').then((m) => m.Login),
  },
  {
    path: 'register',
    loadComponent: () => import('./pages/register/register').then((m) => m.Register),
  },
  {
    path: 'complete-profile',
    loadComponent: () =>
      import('./pages/complete-profile/complete-profile').then((m) => m.CompleteProfile),
  },
  {
    path: 'activate',
    loadComponent: () => import('./pages/activate/activate').then((m) => m.Activate),
  },
  {
    path: 'resend-activation',
    loadComponent: () =>
      import('./pages/resend-activation/resend-activation').then((m) => m.ResendActivation),
  },
  {
    path: 'forgot-password',
    loadComponent: () =>
      import('./pages/forgot-password/forgot-password').then((m) => m.ForgotPassword),
  },
  {
    path: 'reset-password',
    loadComponent: () =>
      import('./pages/reset-password/reset-password').then((m) => m.ResetPassword),
  },
  {
    path: 'verify-2fa',
    loadComponent: () =>
      import('./pages/verify-two-factor/verify-two-factor').then((m) => m.VerifyTwoFactor),
  },
  {
    path: 'change-temp-password',
    loadComponent: () =>
      import('./pages/change-temp-password/change-temp-password').then((m) => m.ChangeTempPassword),
  },
  {
    path: 'dashboard',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    // US-13 AC2: non-owner Tenant users are hard-blocked from this route at the UI
    // layer, mirroring the API's TenantHardBlockAuthorizationHandler.
    path: 'ledger',
    canActivate: [authGuard, denyRolesGuard([RoleNames.Tenant])],
    loadComponent: () => import('./pages/ledger/ledger').then((m) => m.Ledger),
  },
  {
    // US-20: "Prohibited Roles: Non-owner Tenants (Tenant)" -- unlike US-19/21/22, Vendor
    // is not prohibited here, per that story's own acceptance criteria.
    path: 'properties',
    canActivate: [authGuard, denyRolesGuard([RoleNames.Tenant])],
    loadComponent: () =>
      import('./pages/properties/property-list/property-list').then((m) => m.PropertyList),
  },
  {
    // US-21: "Prohibited Roles: Non-owner Tenants (Tenant) and Vendors (Vendor)."
    path: 'properties/import',
    canActivate: [authGuard, denyRolesGuard([RoleNames.Tenant, RoleNames.Vendor])],
    loadComponent: () =>
      import('./pages/properties/property-import/property-import').then((m) => m.PropertyImport),
  },
  {
    // US-19: "Prohibited Roles: Non-owner Tenants and Vendors" on every Sprint 3 story.
    path: 'properties/new',
    canActivate: [authGuard, denyRolesGuard([RoleNames.Tenant, RoleNames.Vendor])],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () =>
      import('./pages/properties/property-form-container/property-form-container').then(
        (m) => m.PropertyFormContainer,
      ),
  },
  {
    // US-29: "Prohibited Roles: Non-owner Tenants, Vendors" -- must be registered before
    // properties/:id, otherwise the :id route would swallow "matrix" as a property id.
    path: 'properties/matrix',
    canActivate: [authGuard, denyRolesGuard([RoleNames.Tenant, RoleNames.Vendor])],
    loadComponent: () =>
      import('./pages/properties/property-matrix/property-matrix').then((m) => m.PropertyMatrix),
  },
  {
    path: 'properties/:id',
    canActivate: [authGuard, denyRolesGuard([RoleNames.Tenant, RoleNames.Vendor])],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () =>
      import('./pages/properties/property-form-container/property-form-container').then(
        (m) => m.PropertyFormContainer,
      ),
  },
  {
    // US-33/US-36: the unit's lifetime financial statement, which in this flattened
    // Property model (one row = one door) also serves as US-36's per-property ledger.
    // Permissions.Ledger.Read isn't granted to Tenant today, so this is PM/Owner/Accountant
    // only for now -- a resident-facing "view my own unit's statement" scope is a separate,
    // not-yet-built feature, not what "gated strictly to their own unit statement view" in
    // the acceptance criteria describes building this sprint.
    path: 'properties/:id/ledger',
    canActivate: [authGuard, denyRolesGuard([RoleNames.Tenant, RoleNames.Vendor])],
    loadComponent: () =>
      import('./pages/properties/unit-statement/unit-statement').then((m) => m.UnitStatement),
  },
  {
    // US-40: embeds a statement or payment-receipt PDF (distinguished by the `type` query
    // param) -- same role gating as the statement page it's launched from.
    path: 'properties/:id/ledger/pdf',
    canActivate: [authGuard, denyRolesGuard([RoleNames.Tenant, RoleNames.Vendor])],
    loadComponent: () => import('./pages/properties/pdf-viewer/pdf-viewer').then((m) => m.PdfViewer),
  },
  {
    // Audit Refinement Sprint: US-25's community directory -- Permissions.Directory.Read is
    // only granted to Tenant and SuperAdmin (Permissions.All) in RolePermissions.Bundles, so
    // every other role is denied here too, mirroring the API's own permission grant at the UI
    // layer (same "hard-block at both layers" convention as the /ledger route above).
    path: 'directory',
    canActivate: [
      authGuard,
      denyRolesGuard([
        RoleNames.PropertyManager,
        RoleNames.BoardMember,
        RoleNames.PropertyOwner,
        RoleNames.Vendor,
        RoleNames.CommitteeMember,
        RoleNames.OnSiteStaff,
        RoleNames.Accountant,
      ]),
    ],
    loadComponent: () => import('./pages/directory/directory').then((m) => m.Directory),
  },
  {
    // PM-facing verification view of the same directory (Permissions.Resident.Read instead
    // of Directory.Read) -- only PropertyManager has that grant (SuperAdmin via
    // Permissions.All), so every other role is denied here too.
    path: 'admin/directory',
    canActivate: [
      authGuard,
      denyRolesGuard([
        RoleNames.BoardMember,
        RoleNames.PropertyOwner,
        RoleNames.Tenant,
        RoleNames.Vendor,
        RoleNames.CommitteeMember,
        RoleNames.OnSiteStaff,
        RoleNames.Accountant,
      ]),
    ],
    loadComponent: () => import('./pages/directory-admin/directory-admin').then((m) => m.DirectoryAdmin),
  },
  {
    // Refinement Sprint (Directive 4): workspace-wide admin toggles. Matches
    // Permissions.Workspace.SettingsRead/Write's grant list (PropertyManager, BoardMember,
    // SuperAdmin) -- everyone else is denied at the UI layer too, mirroring the API's policy.
    path: 'admin/settings',
    canActivate: [
      authGuard,
      denyRolesGuard([
        RoleNames.PropertyOwner,
        RoleNames.Tenant,
        RoleNames.Vendor,
        RoleNames.CommitteeMember,
        RoleNames.OnSiteStaff,
        RoleNames.Accountant,
      ]),
    ],
    loadComponent: () => import('./pages/admin-settings/admin-settings').then((m) => m.AdminSettings),
  },
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: '**', redirectTo: 'dashboard' },
];
