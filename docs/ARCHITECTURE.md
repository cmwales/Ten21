# ARCHITECTURE SPECIFICATION

## 1. Multi-Tenancy Isolation Strategy

- Database Model: Single Shared Database with Global Query Filters (Baseline for cost-effective self-serve HOAs).
- Tenant Scoping: Every domain entity implements `ITenantScopedEntity` carrying a required `TenantId` (Guid) property.
- Enforcing Data Isolation:
  - EF Core `OnModelCreating` applies `HasQueryFilter(e => e.TenantId == _currentTenantId)` via reflection across all tenant-scoped entities.
  - Database-level Row-Level Security (RLS) policies enforce connection-level isolation.
  - `TenantMiddleware` intercepts incoming Web API requests, extracts `tenant_id` from JWT claims, and injects context into a scoped `ITenantContext` service.
- Onboarding Flow: Automated zero-touch self-serve provisioning inserting a new record into `Tenants` table with zero database-per-tenant deployment overhead.

## 2. Premium Enterprise Escalation Path

- Database-per-Tenant Support: Architecture supports routing high-tier/enterprise tenants to physically isolated databases via custom per-tenant connection strings configured in `DbContextFactory`.

## 3. Parent Organization & Multi-Property Management Hierarchy

- Parent-Child Tenant Scoping: Property Management Companies (PMCs) operate as parent `Organizations`. Individual managed HOAs or property corporations retain distinct, isolated `TenantId` records to maintain strict legal, financial, and database boundaries.
- Multi-Tenant Context Switching: PMC staff authenticate once. Their JWT contains authorized claims for all child `TenantId` entities under their parent `OrganizationId`. The UI provides seamless context switching while EF Core `HasQueryFilter` automatically restricts query execution to the actively selected `TenantId`.
- Admin Portal Boundaries:
  - Platform Admin Portal (`Platform SuperAdmin`): Internal platform operations for global tenant provisioning, subscription management, system diagnostics, and cross-tenant support.
  - PMC Portfolio Dashboard (`Property Manager`): Client-facing management interface for PMC executives and property managers to manage staff permissions, route work orders, and oversee financials across their specific managed portfolio.

## 4. Layered Architecture & the Business Service Layer

- Project chain: `Ten21.Domain` → `Ten21.Application` → `Ten21.Infrastructure` → `Ten21.Business` → `Ten21.Api`. `Ten21.Business` sits after Infrastructure (not between Application and Infrastructure) because Infrastructure already depends on Application to implement its interfaces -- Application depending back on Infrastructure/Business for `Ten21DbContext` would be circular.
- `Ten21.Api` is the composition root and the HTTP boundary only: controllers receive/send DTOs, apply `[Authorize]` policies, and resolve HTTP-specific concerns (`ClaimsPrincipal`/`User.FindFirst`, `HttpContext`, `Request.Cookies`, `Response`, `IWebHostEnvironment` for cookie flags). **No controller injects or queries `Ten21DbContext` directly.** HTTP-specific values are extracted to plain parameters (`Guid userId`, `string? clientIp`, a raw cookie string, an `IFormFile`) and passed into a Business service.
- `Ten21.Business` holds one concrete service class per domain area (e.g. `ChargeService`, `PropertyService`, `AuthService`), each depending on `Ten21DbContext` directly for simple single-table operations, plus a concrete `*Repository` class only where a genuinely multi-table or batched query is reused as a unit. No interface is created per class -- DI registers and injects concrete types (`services.AddScoped<ChargeService>()`) unless a real second implementation or abstraction need already exists (those interfaces live in `Ten21.Application.Abstractions`: `IInputSanitizer`, `IEmailSender`, `IJwtTokenService`, `IS3StorageService`, etc.).
- **Unit-of-work / `SaveChangesAsync` ownership:** the Business Service that coordinates a complete business operation owns `SaveChangesAsync()` -- it is called once, after every change for that operation has been staged, never independently by a repository. A multi-save workflow that genuinely needs atomicity uses an explicit `Database.BeginTransactionAsync`, owned and committed by the Business Service, never a repository.
- **Resource-based BOLA/IDOR authorization** (`SameTenantResourceAuthorizationHandler`, called via `IAuthorizationService.EnsureSameTenantAsync`) stays in the controller, as a two-step pattern -- resolve, then guard, never combined into one call: `var x = await _xService.FindAsync(propertyId, id, ct) ?? throw new NotFoundException(msg);` immediately followed by `await _authorizationService.EnsureSameTenantAsync(User, x, msg, ct);`. `EnsureSameTenantAsync` is `void`/`Task`-returning and takes a non-null, already-loaded entity -- it is a guard clause that independently re-verifies the entity's `TenantId` against the caller's tenant and throws on mismatch, not a lookup, and must never be asked to also produce the entity or accept a nullable one. Both calls throw `NotFoundException` (never `ForbiddenException`), so a cross-tenant probe is indistinguishable from a missing resource. Keep the two lines adjacent with no logic between them; only afterward is the entity passed into subsequent Service calls.
- `UserManager<ApplicationUser>`/`RoleManager<ApplicationRole>` (ASP.NET Core Identity) are treated as legitimate direct Business Service dependencies, the same tier as `Ten21DbContext` -- they are not considered HTTP-request-specific the way `ClaimsPrincipal`/`HttpContext` are.

## 5. Single-Master Stylesheet & Geographic Hosting Scope

- Single Master Stylesheet Enforcement: Component-level stylesheet generation is disabled by default via Angular CLI schematics (`--inline-style=true`). All design tokens, Tailwind base layers, and reusable UI utility components reside exclusively in a central `styles.scss` file.
- Initial North American Hosting Scope: Platform hosting is optimized strictly for North American tenants (USA & Canada). Multi-region European database deployment and GDPR cross-border isolation are deferred to future enterprise expansion phases.
- Production Domain Mapping: All static SPA assets, public SSR landing routes, and CORS-allowed API origins are locked exclusively to `ten21.io` subdomains.
