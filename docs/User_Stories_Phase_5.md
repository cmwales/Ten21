# Ten21 - Phase 5 Zero-Touch Self Onboarding & User Stories

This document covers **Phase 5: Zero-Touch Self Onboarding** (`ROADMAP.md`) — self-service
workspace registration, Google Sign-In, email verification/password recovery, email-based
2FA, and bot/abuse defense on the public auth surface. Follows the same format as
`User_Stories_Phase_0.md`. Per `FEATURES.md`'s Feature Specification Standards, every story
below declares its Primary Role, Authorized Secondary Roles, Prohibited Roles, and Required
Permission Claims — most of this phase runs pre-authentication (`[AllowAnonymous]`), so
those sections mostly read "N/A (anonymous/public endpoint)" by design, not omission.

## 1. Executive Summary & Core Design Directives

- **Instant provisioning, not a review queue:** `POST /api/auth/register` (US-14) creates the
  `ApplicationUser`, a brand-new `Tenant` (the "workspace"), and issues a full JWT in one
  call — matching `ROADMAP.md`'s "instant tenant provisioning" wording. Email confirmation
  (US-16) is a nudge, not a login gate: an unconfirmed user can still use the product.
- **`DevSeeder` retired.** It was an explicit stopgap ("delete this class rather than extend
  it" once a real registration endpoint exists — see its own class comment and the backend
  README). US-14 is that endpoint; `DevSeeder` and its call in `Program.cs` are removed as
  part of that story, not left to rot alongside the real thing.
- **Google Sign-In reuses ASP.NET Core Identity's built-in external-login plumbing**
  (`UserManager.AddLoginAsync`/`FindByLoginAsync` against the already-existing
  `user_logins` table) rather than a bespoke `GoogleId` column — one fewer schema decision,
  and it composes with any other external provider added later without a migration.
- **Two-tier token model for incomplete state:** both "Google account with no workspace yet"
  (US-15) and "password correct, 2FA still pending" (US-17) need a token that identifies the
  caller but authorizes nothing tenant-scoped yet. Both reuse the same pattern — a
  short-lived, tenant-less JWT carrying only `user_id` and a purpose claim
  (`profile_incomplete` / `2fa_pending`) — instead of two bespoke mechanisms. Because it
  carries no `tenant_id`, every existing tenant-scoped endpoint already fail-closes against
  it for free (`Ten21DbContext`'s fail-closed query filter, US-01) — the same
  defense-in-depth property the rest of the codebase relies on, not a new invariant to trust.
  Implementing this surfaced (and fixed) a real gap: `TenantMiddleware` only ever set
  `ITenantContext.UserId` when a `tenant_id` claim was ALSO present, so an interim token's
  audit trail (`AuditSaveChangesInterceptor`, US-07) would have silently recorded no actor
  at all for the `complete-profile` provisioning it makes. The two claim checks are now
  independent.
- **Turnstile ships wired to a real site/secret key pair** (provided directly by the
  Founder for this project — `Turnstile:SecretKey` via `dotnet user-secrets`, never
  committed; the site key is public by design and lives directly in the frontend). Server
  verification (`TurnstileVerificationService`) gates on three conditions per Cloudflare's
  own guidance — `success`, `action == "register"` (skipped only when Cloudflare omits
  `action` entirely, which only its own published testing secrets do — see the class
  comment), and an allow-listed `hostname` (`Turnstile:AllowedHostnames`, defaults to
  `localhost`; production needs `app.ten21.io` added). Automated tests use Cloudflare's own
  published always-pass testing secret against the *real* siteverify endpoint (no mocking
  of Cloudflare itself) — a developer without the real secret can still run
  `dotnet test`/`ng test` end to end, just not `dotnet run` against a live registration
  form until they have one.
- **SMTP dev-defaults to a no-credential path:** email send defaults to a
  `ConsoleEmailSender` in Development (logs the link/code instead of sending) behind the
  same `IEmailSender` interface a real `SmtpEmailSender` implements — so swapping to
  production SMTP credentials later is a config change, not a code change.
- **Rate limiting is inherited, not re-built.** Every new endpoint in this phase lives on
  `AuthController` (or a controller carrying the same `[EnableRateLimiting(AuthRateLimiterPolicy.PolicyName)]`
  policy from US-05), so "5 req/min per IP on `/api/auth/*`" (US-18's second half) is
  automatic, not a thing each new action has to remember to opt into.

## 2. User Story Summary Matrix

| ID | Story Title | User Story Statement | Core Acceptance Criteria |
| --- | --- | --- | --- |
| **US-14** | Workspace Registration & Onboarding | As a Property Manager or Landlord, I want to sign up by providing my full name, email, phone number, address, workspace identity, and portfolio size, so that my account and primary workspace are provisioned. | `POST /api/auth/register` validates a unique email, records `AgreedToTermsAt`, creates the `ApplicationUser` + a new `Tenant`, seeds a root `TenantMembership` (`IsPrimary = true`) with both `PropertyManager` and `PropertyOwner` role claims, and returns a full `AuthResponse` immediately. |
| **US-15** | Google OAuth & Post-Login Profile Completion | As a User, I want to authenticate using Google Sign-On, so that I can access my workspace without creating a manual password. | `POST /api/auth/google` verifies the Google ID token server-side, auto-links to an existing `ApplicationUser` by email via Identity's external-login store, and — for a brand-new Google signup with no workspace yet — issues an interim token and routes the client to "Complete Your Profile" (phone, address, workspace title, portfolio size) before any tenant-scoped JWT is issued. |
| **US-16** | Email Activation & Self-Service Password Recovery | As a User, I want to receive an email confirmation link and access password reset tools, so that my account identity is verified and recoverable. | Dispatches a tokenized, 24-hour-expiring confirmation link via `IEmailSender`; `POST /api/auth/resend-activation` and the full forgot/reset-password pipeline (`POST /api/auth/forgot-password`, `POST /api/auth/reset-password`) round out the flow, both enumeration-safe (generic response regardless of whether the email exists). |
| **US-17** | Email 6-Digit OTP Dual Authentication (2FA) | As a Security Lead, I want email 6-digit OTP verification for administrative logins, so that account security is enforced without SMS gateway costs or authenticator app lockouts. | Login for `SuperAdmin`/`PropertyManager`/`BoardMember` (SECURITY.md §1's mandatory-MFA roles) or any user with 2FA enabled pauses after password verification, emails a 6-digit code (Identity's built-in `Email` token provider), and requires `POST /api/auth/login/verify-2fa` before a full JWT is issued. Optional TOTP authenticator-app enrollment (`/api/auth/2fa/totp/*`) is available to any user regardless of role. |
| **US-18** | Bot Defense & Registration Rate Limiting | As a CISO, I want bot verification and IP rate limiting on sign-up endpoints, so that automated account creation spam and email flooding are blocked. | `POST /api/auth/register` requires a valid Cloudflare Turnstile token, verified server-side against Cloudflare's `siteverify` endpoint before any account is created. The existing US-05 sliding-window limiter (5 req/min/IP on `/api/auth/*`) already covers every endpoint this phase adds, since they all live under that same policy. |

## 3. Detailed User Stories & Implementation Guidance

### US-14: Workspace Registration & Onboarding

**User Story:** As a Property Manager or Landlord, I want to sign up by providing my full
name, email, phone number, address, workspace identity, and portfolio size, so that my
account and primary workspace are provisioned.

- **Primary Role:** N/A — anonymous/public endpoint (`[AllowAnonymous]`), the same as
  `POST /api/auth/login`.
- **Authorized Secondary Roles:** N/A.
- **Prohibited Roles:** N/A (no authenticated caller exists yet at this endpoint).
- **Required Permission Claims:** N/A at the endpoint itself. The *result* of a successful
  call grants the new user the `PropertyManager` and `PropertyOwner` additive claim bundles
  (`RolePermissions.Bundles`) on their new workspace — see `BUSINESS_RULES.md` §1's
  "Zero-Touch Automated Onboarding" wording.

**Acceptance Criteria:**

- `POST /api/auth/register` accepts `FirstName`, `LastName`, `Email`, `PhoneNumber`,
  `Address`, `WorkspaceName`, `PortfolioSize` (int, unit count), and `AgreedToTerms` (bool,
  must be `true` or the call is rejected with a `ValidationException`).
- Rejects a duplicate email with a clean `ValidationException` (checked via
  `UserManager.FindByEmailAsync` before `CreateAsync`, backstopped by the existing DB-level
  unique index on `NormalizedEmail`) rather than surfacing Identity's raw `IdentityResult`
  error shape.
- Records `AgreedToTermsAt` (new `DateTimeOffset` column on `ApplicationUser`) at the moment
  of registration — never inferred, never defaulted.
- Creates a new `Tenant` (the "workspace") named `WorkspaceName`, with a new `PortfolioSize`
  int column, `OrganizationId = null` (self-managed, matching `Tenant`'s existing "self-managed
  HOAs leave this column NULL" contract).
- Seeds exactly one root `TenantMembership` pair for the new user on the new tenant:
  `PropertyManager` (`IsPrimary = true`) and `PropertyOwner` (`IsPrimary = false`) — a
  self-service landlord is both the operator and the owner of their own portfolio.
- Returns the same `AuthResponse` shape as `POST /api/auth/login` (instant provisioning, no
  separate "now go log in" step) and sets the refresh-token cookie identically.
- `DevSeeder` and its call site in `Program.cs` are deleted — this endpoint is what it was a
  stopgap for.

### US-15: Google OAuth & Post-Login Profile Completion

**User Story:** As a User, I want to authenticate using Google Sign-On, so that I can access
my workspace without creating a manual password.

- **Primary Role:** N/A — anonymous/public endpoint.
- **Authorized Secondary Roles:** N/A.
- **Prohibited Roles:** N/A.
- **Required Permission Claims:** N/A at the endpoint; `POST /api/auth/complete-profile`
  requires an authenticated caller holding the interim `profile_incomplete` token (checked
  explicitly in the action, not via a permission policy — an interim token structurally
  carries no permission claims at all).

**Acceptance Criteria:**

- `POST /api/auth/google` accepts a Google-issued ID token and verifies it server-side
  (`Google.Apis.Auth`) against the configured `Google:ClientId` audience — the token is
  never trusted unverified.
- An existing `ApplicationUser` whose email matches the verified Google payload is
  auto-linked via `UserManager.AddLoginAsync`/`FindByLoginAsync` (Identity's built-in
  external-login store) and receives a normal, full `AuthResponse` for their existing
  primary tenant.
- A first-time Google signup with no matching email creates a new, passwordless
  `ApplicationUser` (`EmailConfirmed = true` — Google already verified it) with **no**
  `Tenant`/`TenantMembership` yet, and returns an interim response (`requiresProfileCompletion:
  true` + a short-lived, tenant-less token) instead of a full `AuthResponse`.
- `POST /api/auth/complete-profile` (interim-token-authenticated) accepts the same
  workspace fields as US-14's registration (`PhoneNumber`, `Address`, `WorkspaceName`,
  `PortfolioSize`), then runs the identical tenant + root-membership provisioning as US-14
  and returns the final, full `AuthResponse`.
- Frontend: a "Complete Your Profile" route the client is redirected to whenever
  `requiresProfileCompletion` comes back from `/api/auth/google`.

### US-16: Email Activation & Self-Service Password Recovery

**User Story:** As a User, I want to receive an email confirmation link and access password
reset tools, so that my account identity is verified and recoverable.

- **Primary Role:** N/A — anonymous/public endpoints for resend/forgot/reset; the activation
  link itself is opened unauthenticated.
- **Authorized Secondary Roles:** N/A.
- **Prohibited Roles:** N/A.
- **Required Permission Claims:** N/A.

**Acceptance Criteria:**

- On registration (US-14) and on-demand (`POST /api/auth/resend-activation`), dispatches a
  tokenized confirmation link (`UserManager.GenerateEmailConfirmationTokenAsync`) via
  `IEmailSender`, expiring in 24 hours (Identity's `DataProtectionTokenProviderOptions.TokenLifespan`,
  set explicitly rather than left implicit).
- `POST /api/auth/activate` (`UserId` + `Token`) confirms the account
  (`UserManager.ConfirmEmailAsync`). Email confirmation is a status flag, never a login
  gate — an unconfirmed user can still authenticate (see Executive Summary).
- `POST /api/auth/forgot-password` and `POST /api/auth/reset-password` round-trip a
  tokenized reset (`GeneratePasswordResetTokenAsync` / `ResetPasswordAsync`). Both
  `resend-activation` and `forgot-password` return an identical generic response whether or
  not the email exists — the same enumeration-safe pattern `AuthController.Login` already
  uses for bad credentials.
- `IEmailSender` (Application.Abstractions) has two implementations: `SmtpEmailSender`
  (Infrastructure, real SMTP via the configured `cmwales@gmail.com` gateway) and
  `ConsoleEmailSender` (Infrastructure, dev-only — logs the link/code instead of sending).
  Registration (`EmailServiceCollectionExtensions.AddEmail`) picks one based on whether
  `Smtp:Username`/`Smtp:Password` are configured — not an environment check — so the flow
  is testable with zero mailbox setup by default, and a real SMTP gateway works the moment
  credentials exist, in any environment.
- A successful `reset-password` also calls the new `IRefreshTokenService.RevokeAllForUserAsync`,
  killing every other currently-active session (across every tenant) for that account — a
  token issued under the old password shouldn't silently keep working after a reset,
  especially since a reset is often the recovery action for a compromised account.
- Frontend: an activation-landing route, a "Resend Activation Email" affordance, and
  forgot/reset-password forms.

### US-17: Email 6-Digit OTP Dual Authentication (2FA)

**User Story:** As a Security Lead, I want email 6-digit OTP verification for
administrative logins, so that account security is enforced without SMS gateway costs or
authenticator app lockouts.

- **Primary Role:** SuperAdmin, PropertyManager, BoardMember (SECURITY.md §1's mandatory-MFA
  roles) — 2FA is required at login for these roles regardless of individual preference.
- **Authorized Secondary Roles:** Any role, opt-in — `TwoFactorEnabled` can be turned on by
  any user for their own account (SECURITY.md §1's "optional/adaptive MFA... for Residents
  by default").
- **Prohibited Roles:** N/A.
- **Required Permission Claims:** N/A — this gates *login itself*, before any permission
  claim would apply.

**Acceptance Criteria:**

- After password verification, if the caller's primary role is one of the mandatory-MFA
  roles, or the account has `TwoFactorEnabled = true` (which, per `MandatoryTwoFactorRoles`'s
  class comment, only ever becomes true via a completed TOTP enrollment — never toggled by
  the mandatory-role check itself), login does **not** yet return an `AuthResponse`. It picks
  a provider — `Authenticator` if `TwoFactorEnabled`, else `Email` — generates the code
  (email only: `UserManager.GenerateTwoFactorTokenAsync(user, provider)`; a real
  authenticator code is never emailed, the user reads it from their own app), and returns
  `TwoFactorRequiredResponse { requiresTwoFactor: true, method, challengeToken, expiresAtUtc }`.
  The challenge token (`IJwtTokenService.GenerateTwoFactorChallengeToken`) carries the chosen
  provider as an extra claim, so `verify-2fa` checks the code against that SPECIFIC provider
  — an email code can't be replayed as an authenticator code or vice versa.
- `POST /api/auth/login/verify-2fa` (`challengeToken` as bearer + `code` in body) verifies
  via `UserManager.VerifyTwoFactorTokenAsync` and, on success, issues the normal full
  `AuthResponse` via the same `IssueTokensAsync` path as a direct login.
- `POST /api/auth/2fa/totp/setup`, `/enable`, `/disable` let any user optionally enroll an
  authenticator app instead of email OTP (Identity's built-in `Authenticator` token
  provider — no third-party TOTP library needed). All three call a new `EnsureFullSession()`
  guard rejecting any token carrying a `purpose` claim (profile-incomplete OR 2fa-pending) —
  added after this story's own integration tests surfaced that without it, a caller holding
  only a password-derived 2fa-pending challenge token (i.e. who has NOT yet proven the
  second factor) could call `disable` and strip 2FA outright. `GetCurrentUserAsync` alone
  doesn't catch this since both token kinds carry the same `user_id` claim it reads.
- **A real gotcha worth flagging for anyone testing this by hand**:
  `UserManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider)`
  always returns `""` — `AuthenticatorTokenProvider.GenerateAsync` is a deliberate no-op
  (displaying a TOTP code server-side isn't meaningful; only the app shows it), confirmed by
  direct probe. `TotpEndToEndTests` computes real RFC 6238 codes by hand (HMAC-SHA1, 30s
  step, 6 digits) against the real shared key instead, the same way a physical authenticator
  app would.
- Frontend: an OTP-entry step (`/verify-2fa`) shown whenever login responds with
  `requiresTwoFactor`, and a `/security` settings page for TOTP enrollment (setup key +
  confirm code, plus a disable action). That page doesn't display live "is TOTP currently
  enabled" status — no `GET` status endpoint exists yet — it just offers both actions
  honestly rather than showing a state it can't back up; a real status query is a reasonable
  follow-up, not built here.

### US-18: Bot Defense & Registration Rate Limiting

**User Story:** As a CISO, I want bot verification and IP rate limiting on sign-up
endpoints, so that automated account creation spam and email flooding are blocked.

- **Primary Role:** N/A — hardens an anonymous/public endpoint.
- **Authorized Secondary Roles:** N/A.
- **Prohibited Roles:** N/A.
- **Required Permission Claims:** N/A.

**Acceptance Criteria:**

- `POST /api/auth/register` requires a Cloudflare Turnstile response token
  (`RegisterRequest.TurnstileToken`) from a widget rendered with `data-action="register"`;
  the server (`TurnstileVerificationService`) verifies it against Cloudflare's `siteverify`
  endpoint (secret key + token + caller IP), gating on `success`, the reported `action`, and
  an allow-listed `hostname`, before creating any account. Bot-defense runs before any other
  registration validation, so a failing request never reaches an email-uniqueness DB lookup.
  Rejects with a `ValidationException` on any failure.
- Wired to a real Cloudflare site/secret key pair for this project (`Turnstile:SecretKey`
  via `dotnet user-secrets`, never committed; the site key is public and lives directly in
  `frontend/src/app/core/turnstile/turnstile.ts`). Automated tests instead use Cloudflare's
  own published always-pass testing secret against the real `siteverify` endpoint, so
  `dotnet test`/`ng test` need no credential at all — only `dotnet run` against a live
  registration form needs the real secret configured.
- `AuthRateLimiterPolicy` (US-05, already 5 req/min/IP sliding window on `/api/auth/*`) is
  verified to cover every endpoint this phase adds by construction (same controller, same
  `[EnableRateLimiting]` attribute) — no new limiter logic needed.
