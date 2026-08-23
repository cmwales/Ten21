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

## 4. Single-Master Stylesheet & Geographic Hosting Scope

- Single Master Stylesheet Enforcement: Component-level stylesheet generation is disabled by default via Angular CLI schematics (`--inline-style=true`). All design tokens, Tailwind base layers, and reusable UI utility components reside exclusively in a central `styles.scss` file.
- Initial North American Hosting Scope: Platform hosting is optimized strictly for North American tenants (USA & Canada). Multi-region European database deployment and GDPR cross-border isolation are deferred to future enterprise expansion phases.
- Production Domain Mapping: All static SPA assets, public SSR landing routes, and CORS-allowed API origins are locked exclusively to `ten21.io` subdomains.
