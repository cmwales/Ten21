/**
 * Mirrors Ten21.Api.Contracts.ApiResponse<T> (US-08) — every 2xx response from the
 * backend is wrapped in this envelope, including these auth responses.
 */
export interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  message: string | null;
  statusCode: number;
  traceId: string;
}

/** Mirrors Ten21.Api.Contracts.Auth.LoginRequest. */
export interface LoginRequest {
  email: string;
  password: string;
}

/** Mirrors Ten21.Api.Contracts.Auth.RegisterRequest (US-14, +turnstileToken in US-18). */
export interface RegisterRequest {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  phoneNumber: string | null;
  address: string | null;
  workspaceName: string;
  portfolioSize: number;
  agreedToTerms: boolean;
  turnstileToken: string;
}

/**
 * Mirrors Ten21.Api.Contracts.Auth.AuthResponse. The refresh token never appears here —
 * it only ever travels as the ten21_refresh_token HTTP-only cookie (SECURITY.md §2).
 */
export interface AuthResponse {
  accessToken: string;
  expiresAtUtc: string;
  tenantId: string;
  organizationId: string | null;
  role: string;
}

/** Mirrors Ten21.Api.Contracts.Auth.ProfileCompletionRequiredResponse (US-15). Returned by
 * POST /api/auth/google instead of AuthResponse when a first-time Google signup has no
 * workspace yet. */
export interface ProfileCompletionRequiredResponse {
  requiresProfileCompletion: true;
  interimToken: string;
  expiresAtUtc: string;
}

/** Mirrors Ten21.Api.Contracts.Auth.CompleteProfileRequest (US-15). */
export interface CompleteProfileRequest {
  phoneNumber: string | null;
  address: string | null;
  workspaceName: string;
  portfolioSize: number;
}

/** Mirrors Ten21.Api.Contracts.Auth.GenericAcknowledgementResponse (US-16) -- the
 * enumeration-safe response resend-activation and forgot-password both return. */
export interface GenericAcknowledgementResponse {
  message: string;
}

/** Mirrors Ten21.Api.Contracts.Auth.TwoFactorRequiredResponse (US-17). Returned by
 * POST /api/auth/login instead of AuthResponse when the password check succeeds but a
 * code is still required. */
export interface TwoFactorRequiredResponse {
  requiresTwoFactor: true;
  method: 'Email' | 'Authenticator';
  challengeToken: string;
  expiresAtUtc: string;
}

/** Mirrors Ten21.Api.Contracts.Auth.TotpSetupResponse (US-17). */
export interface TotpSetupResponse {
  sharedKey: string;
  otpAuthUri: string;
}

/** Mirrors the RFC 7807 ProblemDetails shape produced by GlobalExceptionHandler (US-09). */
export interface ProblemDetails {
  status: number;
  title: string;
  type: string;
  detail: string;
  instance: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}
