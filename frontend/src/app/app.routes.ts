import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { denyRolesGuard } from './core/guards/role.guard';
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
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: '**', redirectTo: 'dashboard' },
];
