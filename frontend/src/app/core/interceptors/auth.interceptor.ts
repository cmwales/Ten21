import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * Login is deliberately excluded from this interceptor's error handling (US-11 AC3):
 * a 401 there is expected application flow (bad password, lockout) that the login page
 * itself must display, not a session-expiry signal that should bounce the user back to
 * /login they're already on. /login/verify-2fa (US-17) falls under this same exclusion
 * today only because its URL happens to start with LOGIN_URL's string ('/api/auth/login') --
 * confirmed intentional-enough to leave as is, but fragile: if LOGIN_URL's matching logic
 * or value ever changes, verify-2fa's 401s (an invalid/expired code -- also expected
 * application flow, not a session-expiry signal) would silently start being caught here,
 * triggering a doomed silent-refresh attempt (there is no session yet at this point in the
 * flow) and bouncing the user to /login instead of showing an inline error on the page
 * they're on.
 */
const LOGIN_URL = '/api/auth/login';
const REFRESH_URL = '/api/auth/refresh-token';

/**
 * US-11: attaches the bearer access token to every /api request and sends the
 * ten21_refresh_token cookie (withCredentials) so refresh/revoke work. On a 401 from any
 * endpoint other than login/refresh itself, attempts one silent refresh-and-retry before
 * giving up; any 401 that survives that (or any 403) clears the session and redirects to
 * /login.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!req.url.startsWith('/api/')) {
    return next(req);
  }

  const token = authService.accessToken();
  const authReq = req.clone({
    withCredentials: true,
    setHeaders: token ? { Authorization: `Bearer ${token}` } : {},
  });

  if (req.url.includes(LOGIN_URL)) {
    return next(authReq);
  }

  return next(authReq).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      if (error.status === 401 && !req.url.includes(REFRESH_URL)) {
        return authService.refresh().pipe(
          switchMap((session) =>
            next(
              authReq.clone({
                setHeaders: { Authorization: `Bearer ${session.accessToken}` },
              }),
            ),
          ),
          catchError((refreshError: unknown) => {
            authService.clearSession();
            void router.navigate(['/login']);
            return throwError(() => refreshError);
          }),
        );
      }

      if (error.status === 401 || error.status === 403) {
        authService.clearSession();
        void router.navigate(['/login']);
      }

      return throwError(() => error);
    }),
  );
};
