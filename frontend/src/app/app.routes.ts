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
