import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { RoleName } from '../constants/roles';

/**
 * US-13 AC2: hard-blocks specific roles from a route at the UI layer — e.g. non-owner
 * Tenant users from owner financial/ledger views (SECURITY.md §4.2's Owner vs. Tenant
 * Isolation Principle). Mirrors, but does not replace, the server-side
 * TenantHardBlockAuthorizationHandler — the real enforcement boundary is still the API.
 */
export function denyRolesGuard(deniedRoles: readonly RoleName[]): CanActivateFn {
  return () => {
    const authService = inject(AuthService);
    const router = inject(Router);

    if (!authService.isAuthenticated()) {
      return router.createUrlTree(['/login']);
    }

    const role = authService.role();
    return role !== null && (deniedRoles as readonly string[]).includes(role)
      ? router.createUrlTree(['/dashboard'])
      : true;
  };
}
