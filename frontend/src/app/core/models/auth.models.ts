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
