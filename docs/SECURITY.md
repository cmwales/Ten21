# SECURITY SPECIFICATION

## 1. Authentication & Identity Management

- Core Framework: Built on standard ASP.NET Core Identity (`Microsoft.AspNetCore.Identity`) utilizing native secure password hashing (PBKDF2 / Argon2).
- Token Model: Web API issues stateless JWT Access Tokens and secure HTTP-Only Refresh Tokens containing `tenant_id`, `user_id`, and `role` claims.
- Multi-Factor Authentication (MFA) Policy:
  - Mandatory MFA: Enforced for SuperAdmins, Board Members, and Property Managers via a 6-digit code emailed at sign-in (Identity's built-in email token provider). No authenticator-app (TOTP) option — see `User_Stories_Phase_5.md` US-17 for why.
  - Optional / Adaptive MFA: Optional for Residents by default; triggered automatically on high-risk actions (modifying bank/payment details, updating primary email/password, or logging in from unrecognized devices).
- Account Lockout & Rate Limiting:
  - Account Lockout: Configured via `IdentityOptions` (5 consecutive failed login attempts locks the account for a 15-minute window).
  - Rate Limiting: IP-based sliding window rate limiting (max 5 requests per minute on `/api/auth/*` endpoints) enforced via native `Microsoft.AspNetCore.RateLimiting` middleware.

## 2. Multi-Tenant Data Isolation & Data Protection

- Tenant Isolation: Single shared database protected at the ORM layer by mandatory EF Core `HasQueryFilter(e => e.TenantId == _currentTenantId)` and database-level Row-Level Security (RLS).
- Payment Data Isolation (Zero Raw Financial Data Policy):
  - No raw bank account numbers, routing numbers, or credit card details are ever transmitted to or stored on self-hosted application servers or databases.
  - Payment Tokenization: All ACH and financial payment instrument collection is delegated to an external payment processor SDK (e.g., Stripe/Plaid). Only public token references (e.g., `pm_1N9x...`) are retained in the database.
- Sensitive PII Protection (Self-Hosted): Non-payment sensitive PII (e.g., SSNs, Tax IDs) is encrypted at the application layer using ASP.NET Core Data Protection API with self-hosted certificate or file key persistence.

## 3. Web Application Security & OWASP Hardening

- XSS Guard: Strict use of Angular built-in DOM Sanitizer on frontend; input sanitization on Web API endpoints.
- CSRF Defense: Stateless JWTs passed strictly via `Authorization: Bearer` HTTP headers, eliminating ambient cookie-based CSRF attack vectors.
- SQL Injection Guard: Exclusive reliance on EF Core parameterized queries; raw unparameterized SQL is strictly prohibited.
- BOLA / IDOR Defense: Double-layer resource validation combining EF Core tenant query filters with resource-based ASP.NET Authorization Handlers inspecting entity ownership.
- Security Headers: Middleware enforces `Content-Security-Policy (CSP)`, `Strict-Transport-Security (HSTS)`, `X-Content-Type-Options: nosniff`, and `X-Frame-Options: DENY`.

## 4. Role Taxonomy & Access Control System

### 4.1 System Role Taxonomy (9 Tiers)

- Platform SuperAdmin: Internal platform administration, tenant provisioning, and global diagnostics across all HOAs/PMCs.
- Property Manager: PMC staff or Community Association Manager handling day-to-day operations, vendor routing, work orders, and community admin. Cannot cast HOA board votes.
- Board Member: Elected HOA board director with fiduciary oversight, financial ledger access, executive notes, and official community voting setup.
- Property Owner: Deed/Title holder (resident or off-site landlord) with access to personal dues ledgers, ARC submissions, tax forms, and official owner election ballots.
- Tenant: Non-owner renter occupant restricted strictly to maintenance tickets, amenity booking, and community announcements. Zero access to financial ledgers or voting.
- Vendor: External contractor handling assigned work order status, completion photo uploads, and invoice submissions.
- Committee Member: Volunteer (e.g., ARC / Social committee) granted scoped review and voting rights over specific operational workflows without financial ledger access.
- On-Site Staff: Front desk, concierge, or maintenance employee handling package logging, amenity check-ins, and work order execution.
- Accountant / Bookkeeper: External financial auditor granted read/write ledger access and bank reconciliation rights without administrative or lease management access.

### 4.2 Authorization Architecture & Policy Rules

- Additive Claims-Based RBAC: Roles do not use strict linear inheritance. User JWTs carry additive claim bundles (e.g., `Permissions.Ledger.Read`, `Permissions.ARC.Approve`). Multi-role users (e.g., an Owner who is also a Board Member) accumulate claims dynamically.
- Owner vs. Tenant Isolation Principle: Non-owner renters (`Tenant`) are hard-blocked at the policy layer from accessing financial ledgers, legal notices, delinquency reports, or community voting endpoints.
- Security-First Feature Mandate: Every application endpoint and UI component must map explicitly to a claim-based policy requirement before implementation.

## 5. Organization & Cross-Tenant Authorization Rules

- Portfolio Access Validation: When a PMC user switches active property context, `TenantMiddleware` validates that the target `TenantId` explicitly belongs to the user's authorized `OrganizationId` before issuing or updating session claims.
- Employee Offboarding Revocation: Removing a staff member from a parent `Organization` instantly invalidates their access across all child `TenantId` properties under that PMC portfolio.
