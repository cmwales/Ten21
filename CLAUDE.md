# Ten21 (ten21.io) — Project Instructions for Claude

Ten21 is a domain-agnostic, multi-tenant property management SaaS platform (residential rentals, commercial suites, self-storage, HOAs). We are currently executing **Phase 0: Security \& Multi-Tenancy Baseline** and the **V1 Landlord MVP**. Priorities: ultra-low operating cost, zero-touch self-service provisioning, and strict multi-tenant security guardrails.

Full specs live in `docs/` at the repo root (next to the `.sln` file):
`OVERVIEW.md`, `ARCHITECTURE.md`, `SECURITY.md`, `TECH\_PREFERENCES.md`, `DATA\_MODEL.md`, `MVP\_features.md`, `FEATURES.md`, `BUSINESS\_RULES.md`, `DESIGN\_SYSTEM.md`, `ROADMAP.md`, `SYSTEM\_INSTRUCTIONS.md`, `User\_Stories\_Phase\_0.md`. **Read the relevant doc before implementing anything non-trivial** — this file is a summary/quick-reference, not the source of truth.

## Tech Stack

* Backend: C# .NET 9 Web API + Entity Framework Core
* Database: PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL`, with Row-Level Security (RLS)
* Frontend: Angular (Standalone Components + Signals), Tailwind CSS

  * **Single master `styles.scss`** — do not generate component-level `.scss` files (use `--inline-style=true` / no stylesheet per component)
* Object Storage: AWS S3 / Cloudflare R2 via presigned PUT URLs
* Domain routing: `ten21.io` (marketing/SSR), `app.ten21.io` (web app), `api.ten21.io` (backend API)

## Non-Negotiable Architecture Rules

1. **Multi-Tenancy:** Single shared database. Every tenant-scoped entity implements `ITenantScopedEntity { Guid TenantId }`. Isolation is enforced in *two* layers — never skip either:

   * EF Core: `HasQueryFilter(e => e.TenantId == \_tenantContext.TenantId)` applied via reflection in `OnModelCreating` for all `ITenantScopedEntity` types.
   * Postgres RLS policy keyed on `app.current\_tenant\_id`.
   * `TenantMiddleware` extracts `tenant\_id` from the JWT (or `X-Tenant-Id` header) into a scoped `ITenantContext`. `TenantId` is auto-populated on insert — never trust a client-supplied `TenantId`.
2. **Auth:** ASP.NET Core Identity. Stateless 15-minute JWT access tokens carrying `tenant\_id`, `user\_id`, `organization\_id`, and role/permission claims. 7-day HTTP-Only refresh tokens with rotation. Endpoints: `POST /api/auth/login`, `/refresh-token`, `/revoke-token`.
3. **RBAC (9 roles, additive claims — not linear inheritance):** SuperAdmin, Property Manager, Board Member, Property Owner, Tenant, Vendor, Committee Member, On-Site Staff, Accountant. Every endpoint/UI action maps to a permission claim (e.g. `Permissions.Ledger.Read`, `Permissions.WorkOrders.Write`) checked via policy-based authorization. **Non-owner `Tenant` role is hard-blocked at the policy layer from financial ledgers, legal notices, and voting** — this is a security invariant, not a UX preference.
4. **Organizations / multi-property hierarchy:** PMCs are parent `Organizations`; each managed property/HOA keeps its own isolated `TenantId`. `POST /api/organization/switch-context` validates the target tenant belongs to the caller's `OrganizationId`, then issues a freshly scoped JWT. Never mutate the active tenant without going through this flow.
5. **Security hardening:**

   * Identity lockout: 5 failed attempts → 15-minute lock.
   * Rate limiting: sliding window, 5 req/min on `/api/auth/\*` (native `Microsoft.AspNetCore.RateLimiting`).
   * Zero raw payment data: all bank/card details are tokenized via an external processor SDK; only token references (`pm\_...`) and display metadata (last four, bank name) are ever stored.
   * Sensitive PII (SSN, Tax ID, etc.) encrypted at the application layer via ASP.NET Core Data Protection API (self-hosted key persistence).
   * Standard OWASP hardening: Angular DOM sanitization, parameterized EF Core queries only (no raw SQL), CSP/HSTS/X-Frame-Options/nosniff headers, resource-based authorization handlers for BOLA/IDOR defense (tenant filter is not sufficient by itself).
6. **Object storage:** `IS3StorageService` issues 15-minute presigned PUT URLs scoped to `{TenantId}/{Category}/{EntityId}/{Guid}.ext`. Enforce MIME allowlist (JPEG, PNG, WebP, PDF) and a hard 10MB limit *before* signing, not just client-side.
7. **Audit \& soft delete:** Entities implement `IAuditableEntity` / `ISoftDelete`. A `SaveChangesInterceptor` synchronously captures JSON diffs into `AuditLog` during `SaveChangesAsync` (same transaction). Global query filter enforces `IsDeleted == false` automatically — do not query these entities without it.
8. **API conventions:**

   * All successful responses wrapped in `ApiResponse<T>` (`Success`, `Data`, `Message`, `StatusCode`, `TraceId`).
   * All errors funnel through a centralized `IExceptionHandler` → RFC 7807 `ProblemDetails`. Use the established exception taxonomy (`DomainException`, `NotFoundException`, `ValidationException`, `UnauthorizedException`, `ForbiddenException`, `ConflictException`) rather than throwing raw exceptions or returning ad-hoc error shapes.

## Working Conventions

* Every new feature/task should be traceable to a user story in the "As a \[Role], I want \[Action], so that \[Benefit]" format, with explicit primary role, authorized secondary roles, prohibited roles, and required permission claims (see `FEATURES.md`).
* Every task assumes: EF Core query/filter defined, DTO defined, C# service method, UI component (if applicable), and a unit test — build it as a full vertical slice, not partial layers.
* Write xUnit tests alongside implementation, especially for: tenant isolation boundaries, policy/claim evaluation, lockout \& rate-limit triggers, presigned URL scoping/size limits, soft-delete + audit log behavior, and RFC 7807 response shapes.
* Definition of Done: builds with zero warnings, `dotnet test` passes, tenant isolation (`TenantId`) is enforced on any new entity, changes committed to Git.
* Accessibility: WCAG 2.1 AA minimum — 4.5:1 contrast, `focus-visible:ring-2`, `aria-label`s, 48×48px minimum tap targets. Localize new user-facing strings via `@ngx-translate` (`en-US`, `es-US`, `fr-CA`).
* Don't invent scope: no sci-fi/speculative features, no automated payment processing yet (Phase 2+), no HOA governance/voting UI yet (Phase 2+). Check `ROADMAP.md` before building ahead of the current phase.
* When a decision isn't already covered by a doc in `docs/`, ask rather than assume — architectural and security decisions here are deliberate and should stay consistent project-wide.

