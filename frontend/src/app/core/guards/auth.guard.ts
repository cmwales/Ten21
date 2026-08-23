import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * US-13: hard-blocks navigation into any protected route unless a non-expired access
 * token is present. This is a UX/defense-in-depth layer only — the API enforces the real
 * authorization boundary on every request regardless of what the client-side router lets
 * through.
 */
export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  return authService.isAuthenticated() ? true : router.createUrlTree(['/login']);
};
