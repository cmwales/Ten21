import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, tap } from 'rxjs';
import {
  ApiResponse,
  AuthResponse,
  CompleteProfileRequest,
  GenericAcknowledgementResponse,
  LoginRequest,
  ProfileCompletionRequiredResponse,
  RegisterRequest,
} from '../models/auth.models';

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

  /** US-15: holds the interim (profile_incomplete) token between /api/auth/google
   * returning ProfileCompletionRequiredResponse and the CompleteProfile page submitting
   * it -- deliberately memory-only (not localStorage), since it's short-lived and
   * single-purpose, never a real session. */
  private readonly _interimToken = signal<string | null>(null);
  readonly interimToken = this._interimToken.asReadonly();

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

  /** US-14: workspace registration. Instant provisioning -- succeeds with the same
   * full-session shape as login(), no separate confirmation step required to start using
   * the product. */
  register(request: RegisterRequest): Observable<AuthResponse> {
    return this.http
      .post<ApiResponse<AuthResponse>>('/api/auth/register', request, { withCredentials: true })
      .pipe(
        map((response) => response.data!),
        tap((session) => this.setSession(session)),
      );
  }

  /** US-15: Google Sign-In. Returns either a full AuthResponse (existing account with a
   * workspace -- session is set immediately, same as login()) or a
   * ProfileCompletionRequiredResponse (first-time signup with no workspace yet -- no
   * session is set; the interim token is stashed for CompleteProfile to use next). */
  loginWithGoogle(idToken: string): Observable<AuthResponse | ProfileCompletionRequiredResponse> {
    return this.http
      .post<ApiResponse<AuthResponse | ProfileCompletionRequiredResponse>>(
        '/api/auth/google',
        { idToken },
        { withCredentials: true },
      )
      .pipe(
        map((response) => response.data!),
        tap((result) => {
          if ('requiresProfileCompletion' in result) {
            this._interimToken.set(result.interimToken);
          } else {
            this.setSession(result);
          }
        }),
      );
  }

  /** US-15: the second half of a first-time Google signup. Uses the stashed interim token
   * directly as the Authorization header rather than going through the normal
   * session-based interceptor path -- there is no session yet at this point. */
  completeProfile(request: CompleteProfileRequest): Observable<AuthResponse> {
    const interimToken = this._interimToken();
    if (!interimToken) {
      throw new Error('completeProfile() called with no interim token -- start over at /login.');
    }

    return this.http
      .post<ApiResponse<AuthResponse>>('/api/auth/complete-profile', request, {
        withCredentials: true,
        headers: new HttpHeaders({ Authorization: `Bearer ${interimToken}` }),
      })
      .pipe(
        map((response) => response.data!),
        tap((session) => {
          this._interimToken.set(null);
          this.setSession(session);
        }),
      );
  }

  /** US-16. */
  resendActivation(email: string): Observable<GenericAcknowledgementResponse> {
    return this.http
      .post<ApiResponse<GenericAcknowledgementResponse>>('/api/auth/resend-activation', { email })
      .pipe(map((response) => response.data!));
  }

  /** US-16. */
  activate(userId: string, token: string): Observable<GenericAcknowledgementResponse> {
    return this.http
      .post<ApiResponse<GenericAcknowledgementResponse>>('/api/auth/activate', { userId, token })
      .pipe(map((response) => response.data!));
  }

  /** US-16. */
  forgotPassword(email: string): Observable<GenericAcknowledgementResponse> {
    return this.http
      .post<ApiResponse<GenericAcknowledgementResponse>>('/api/auth/forgot-password', { email })
      .pipe(map((response) => response.data!));
  }

  /** US-16. */
  resetPassword(userId: string, token: string, newPassword: string): Observable<GenericAcknowledgementResponse> {
    return this.http
      .post<ApiResponse<GenericAcknowledgementResponse>>('/api/auth/reset-password', {
        userId,
        token,
        newPassword,
      })
      .pipe(map((response) => response.data!));
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
