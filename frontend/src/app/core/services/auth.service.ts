import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, tap } from 'rxjs';
import { ApiResponse, AuthResponse, LoginRequest } from '../models/auth.models';

const SESSION_STORAGE_KEY = 'ten21_auth_session';

/**
 * US-11: reactive session state + the three auth HTTP calls. The refresh token itself
 * never touches this service or localStorage — it lives only in the ten21_refresh_token
 * HTTP-only cookie the browser attaches automatically (withCredentials: true) per
 * SECURITY.md §2. What IS kept here/in localStorage is the short-lived (15 min) access
 * token and its accompanying claims, so a page reload doesn't force a fresh login while
 * the refresh cookie is still valid.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly _session = signal<AuthResponse | null>(this.readStoredSession());
  readonly session = this._session.asReadonly();

  readonly isAuthenticated = computed(() => {
    const session = this._session();
    return session !== null && new Date(session.expiresAtUtc).getTime() > Date.now();
  });

  readonly role = computed(() => this._session()?.role ?? null);
  readonly tenantId = computed(() => this._session()?.tenantId ?? null);
  readonly organizationId = computed(() => this._session()?.organizationId ?? null);
  readonly accessToken = computed(() => this._session()?.accessToken ?? null);

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http
      .post<ApiResponse<AuthResponse>>('/api/auth/login', request, { withCredentials: true })
      .pipe(
        map((response) => response.data!),
        tap((session) => this.setSession(session)),
      );
  }

  /** Used by the auth interceptor to silently mint a new access token from the refresh cookie. */
  refresh(): Observable<AuthResponse> {
    return this.http
      .post<ApiResponse<AuthResponse>>('/api/auth/refresh-token', {}, { withCredentials: true })
      .pipe(
        map((response) => response.data!),
        tap((session) => this.setSession(session)),
      );
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/revoke-token', {}, { withCredentials: true }).pipe(
      map(() => void 0),
      tap(() => this.clearSession()),
      catchError(() => {
        // Best-effort server-side revocation — clear local state regardless so the UI
        // never gets stuck "logged in" because the network call happened to fail.
        this.clearSession();
        return of(void 0);
      }),
    );
  }

  setSession(session: AuthResponse): void {
    this._session.set(session);
    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
  }

  clearSession(): void {
    this._session.set(null);
    localStorage.removeItem(SESSION_STORAGE_KEY);
  }

  private readStoredSession(): AuthResponse | null {
    try {
      const raw = localStorage.getItem(SESSION_STORAGE_KEY);
      return raw ? (JSON.parse(raw) as AuthResponse) : null;
    } catch {
      return null;
    }
  }
}
