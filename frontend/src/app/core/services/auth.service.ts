import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, tap } from 'rxjs';
import {
  ApiResponse,
  AuthResponse,
  CompleteProfileRequest,
  GenericAcknowledgementResponse,
  LoginRequest,
  PasswordChangeRequiredResponse,
  ProfileCompletionRequiredResponse,
  RegisterRequest,
  TwoFactorRequiredResponse,
} from '../models/auth.models';
import { TenantMembershipSummary } from '../models/organization.models';

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

  /** US-17: holds the pending 2FA challenge between login() returning
   * TwoFactorRequiredResponse and the VerifyTwoFactor page submitting a code -- same
   * memory-only, single-purpose reasoning as _interimToken above. Kept as its own signal
   * (not folded into _interimToken) purely to keep the two flows' state distinct. */
  private readonly _twoFactorChallenge = signal<TwoFactorRequiredResponse | null>(null);
  readonly twoFactorChallenge = this._twoFactorChallenge.asReadonly();

  /** US-24: holds the pending MustChangePassword challenge between login() returning
   * PasswordChangeRequiredResponse and the ChangeTempPassword page submitting a new
   * password -- same memory-only, single-purpose reasoning as _twoFactorChallenge. */
  private readonly _passwordChangeChallenge = signal<PasswordChangeRequiredResponse | null>(null);
  readonly passwordChangeChallenge = this._passwordChangeChallenge.asReadonly();

  readonly isAuthenticated = computed(() => {
    const session = this._session();
    return session !== null && new Date(session.expiresAtUtc).getTime() > Date.now();
  });

  readonly role = computed(() => this._session()?.role ?? null);
  readonly tenantId = computed(() => this._session()?.tenantId ?? null);
  readonly organizationId = computed(() => this._session()?.organizationId ?? null);
  readonly accessToken = computed(() => this._session()?.accessToken ?? null);

  /** US-17/US-24: returns a full AuthResponse (session set immediately, as before), a
   * TwoFactorRequiredResponse (mandatory-role account -- the challenge is stashed for
   * VerifyTwoFactor), or a PasswordChangeRequiredResponse (account provisioned with a
   * temporary password -- the challenge is stashed for ChangeTempPassword). Checked in
   * that order to match AuthController's own precedence (MustChangePassword is checked
   * before 2FA). */
  login(request: LoginRequest): Observable<AuthResponse | TwoFactorRequiredResponse | PasswordChangeRequiredResponse> {
    return this.http
      .post<ApiResponse<AuthResponse | TwoFactorRequiredResponse | PasswordChangeRequiredResponse>>(
        '/api/auth/login',
        request,
        { withCredentials: true },
      )
      .pipe(
        map((response) => response.data!),
        tap((result) => {
          if ('requiresPasswordChange' in result) {
            this._passwordChangeChallenge.set(result);
          } else if ('requiresTwoFactor' in result) {
            this._twoFactorChallenge.set(result);
          } else {
            this.setSession(result);
          }
        }),
      );
  }

  /** US-24: submits a new password against the stashed MustChangePassword challenge --
   * same "use the challenge token directly as the Authorization header" pattern as
   * verifyTwoFactor()/completeProfile(), since there's no session yet. */
  changeTempPassword(newPassword: string): Observable<AuthResponse> {
    const challenge = this._passwordChangeChallenge();
    if (!challenge) {
      throw new Error('changeTempPassword() called with no pending challenge -- start over at /login.');
    }

    return this.http
      .post<ApiResponse<AuthResponse>>(
        '/api/auth/change-temp-password',
        { newPassword },
        { withCredentials: true, headers: new HttpHeaders({ Authorization: `Bearer ${challenge.challengeToken}` }) },
      )
      .pipe(
        map((response) => response.data!),
        tap((session) => {
          this._passwordChangeChallenge.set(null);
          this.setSession(session);
        }),
      );
  }

  /** US-17: submits a code against the stashed challenge -- the challenge token is used
   * directly as the Authorization header, same pattern as completeProfile(), since there's
   * no session yet to drive the normal interceptor path. */
  verifyTwoFactor(code: string): Observable<AuthResponse> {
    const challenge = this._twoFactorChallenge();
    if (!challenge) {
      throw new Error('verifyTwoFactor() called with no pending challenge -- start over at /login.');
    }

    return this.http
      .post<ApiResponse<AuthResponse>>(
        '/api/auth/login/verify-2fa',
        { code },
        { withCredentials: true, headers: new HttpHeaders({ Authorization: `Bearer ${challenge.challengeToken}` }) },
      )
      .pipe(
        map((response) => response.data!),
        tap((session) => {
          this._twoFactorChallenge.set(null);
          this.setSession(session);
        }),
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

  /** US-28: every workspace the caller holds a TenantMembership in, across all tenants --
   * powers the top-nav workspace switcher dropdown. */
  listWorkspaces(): Observable<TenantMembershipSummary[]> {
    return this.http
      .get<ApiResponse<TenantMembershipSummary[]>>('/api/organization/tenants')
      .pipe(map((response) => response.data!));
  }

  /** US-28: switches the active workspace. Reuses setSession() (same as login/refresh), so
   * every signal derived from the session (tenantId, role, organizationId, isAuthenticated)
   * updates reactively for free -- no separate "workspace" signal needed on top of what
   * already exists. Refreshing already-loaded ROUTE DATA (e.g. a property list fetched once
   * in ngOnInit) is a separate concern the caller (AppHeader) handles by re-navigating to
   * the current route, since Angular signals don't retroactively re-trigger a one-time HTTP
   * call just because a value they never subscribed to changed. */
  switchWorkspace(tenantId: string): Observable<AuthResponse> {
    return this.http
      .post<ApiResponse<AuthResponse>>('/api/organization/switch-context', { tenantId }, { withCredentials: true })
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
