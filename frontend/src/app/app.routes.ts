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
    path: 'properties/:id',
    canActivate: [authGuard, denyRolesGuard([RoleNames.Tenant, RoleNames.Vendor])],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () =>
      import('./pages/properties/property-form-container/property-form-container').then(
        (m) => m.PropertyFormContainer,
      ),
  },
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: '**', redirectTo: 'dashboard' },
];
