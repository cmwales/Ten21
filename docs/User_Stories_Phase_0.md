# Ten21 - Phase 0 Foundational Backend Architecture & User Stories

This document outlines the Phase 0 foundational backend architecture and outcome-driven user stories for **Ten21 (ten21.io)** — a multi-tenant property management platform built on **C# .NET 9 Web API** and **Entity Framework Core**. Each task is defined with clear acceptance criteria, technical guardrails, and security boundaries to guide developers without restricting implementation design.

## 1. Executive Summary & Core Architectural Directives

- **Multi-Tenant Data Isolation:** Enforced at the ORM layer using EF Core global query filters (`HasQueryFilter(e => e.TenantId == _tenantContext.TenantId)`) and backed by database-level Row-Level Security (RLS).
- **Parent Organization Hierarchy:** Supports Property Management Companies (PMCs) managing multiple child property entities under an `OrganizationId`. Context switching generates a fresh, scoped JWT carrying the target `tenant_id` claim.
- **Stateless Identity & Authentication:** Built on ASP.NET Core Identity with JWT access tokens and HTTP-Only refresh token rotation.
- **9-Tier Additive Claims RBAC:** Fine-grained policy authorization mapping 9 roles to additive claim bundles, strictly isolating non-owner renters (Tenants) from financial ledgers and voting endpoints.
- **Security Hardening:** Identity brute-force account lockout (5 failed attempts = 15-minute freeze) and sliding-window rate limiting (5 req/min on `/api/auth/*` routes). Zero raw payment data policy.
- **Presigned Object Storage:** AWS S3 / Cloudflare R2 presigned 15-minute PUT URLs scoped to `{TenantId}/{Category}/{EntityId}/{Guid}.ext` with a hard 10MB limit and client-side WebP compression.
- **Audit Logging & Soft Deletes:** EF Core `SaveChangesInterceptor` capturing synchronous JSON entity diffs during `SaveChangesAsync`, with global `IsDeleted == false` query filters.
- **Unified Error Handling & Responses:** Generic `ApiResponse<T>` wrapper for success payloads and a centralized .NET 9 `IExceptionHandler` middleware converting unhandled/domain exceptions into structured RFC 7807 `ProblemDetails`.

## 2. User Story Summary Matrix

| ID | Story Title | User Story Statement | Core Acceptance Criteria |
| --- | --- | --- | --- |
| **US-01** | Multi-Tenant Data Isolation Engine | As a System Architect, I want automatic ORM-level tenant isolation enforced on every query and mutation, so that cross-tenant data leakage is structurally impossible. | Entities implement `ITenantScopedEntity` (`Guid TenantId`). `TenantMiddleware` extracts `tenant_id` from JWT or headers to populate scoped `ITenantContext`. EF Core applies `HasQueryFilter(e => e.TenantId == _tenantContext.TenantId)` globally in `OnModelCreating` and auto-populates `TenantId` on writes. xUnit integration tests prove queries strictly restrict records to active tenant. |
| **US-02** | Identity & Refresh Token Pipeline | As an API Client, I want stateless JWT authentication with HTTP-Only refresh token rotation, so that user sessions remain secure, stateless, and scalable. | Configures ASP.NET Core Identity with custom user, role, and refresh token entities. Issues access tokens carrying `tenant_id`, `user_id`, `organization_id`, and role claims. Exposes typed endpoints: `POST /api/auth/login`, `/refresh-token`, and `/revoke-token`. xUnit tests verify token generation, rotation, and revocation. |
| **US-03** | 9-Tier Additive Claims Authorization Engine | As a Security Lead, I want policy-based authorization mapping our 9 system roles to additive claim bundles, so that granular permissions govern endpoint access. | Maps 9 system roles (SuperAdmin down to Tenant and Vendor) to additive permission claims. Registers permission constants (e.g., `Permissions.Ledger.Read`) and dynamic policies. Custom authorization handlers explicitly block non-owner Tenant users from financial ledgers and voting routes. xUnit tests verify policy evaluation across claim combinations. |
| **US-04** | Parent Organization Hierarchy & Context Switching | As a Property Manager, I want to switch property contexts within my authorized organization, so that I can manage distinct property entities under one account. | Establishes Organization mapping with `OrganizationId` foreign keys on Tenant and ApplicationUser. Middleware validates target property against user's `OrganizationId`. `POST /api/organization/switch-context` returns a newly minted scoped JWT carrying the target `tenant_id`. xUnit tests verify cross-tenant organization boundary validation. |
| **US-05** | Security Hardening & Rate Limiting | As a System Administrator, I want brute-force lockouts and sliding-window rate limiting, so that the API is hardened against abuse. | Configures `IdentityOptions` brute-force lockout (5 failed attempts = 15-minute lock). Applies `Microsoft.AspNetCore.RateLimiting` sliding window (5 req/min per client IP) on `/api/auth/*` routes. xUnit tests verify lockout triggers and HTTP 429 rate limit responses. |
| **US-06** | Presigned Object Storage Service | As an API User, I want to upload document attachments directly to object storage via presigned URLs, so that uploads bypass Web API server memory. | Creates `DocumentAttachment` entity tracking upload metadata. `IS3StorageService` generates 15-minute presigned PUT URLs scoped to `{TenantId}/{Category}/{EntityId}/{Guid}.ext`. Validates allowed MIME types (JPEG, PNG, WebP, PDF) and enforces a hard 10MB limit prior to signing. Exposes `POST /api/documents/presign-upload`. xUnit tests verify presigned key paths and 10MB ceiling enforcement. |
| **US-07** | Audit Logging & Soft Delete Interceptor | As a Compliance Officer, I want automated soft deletes and entity change auditing, so that deleted records are recoverable and state modifications are traceable. | Entities implement `IAuditableEntity` or `ISoftDelete`, backed by `AuditLog` entity. EF Core `SaveChangesInterceptor` captures entity state changes and JSON diffs synchronously during `SaveChangesAsync`. Global query filter applies `IsDeleted == false` automatically. xUnit tests verify soft-deleted exclusion and automatic audit log insertion. |
| **US-08** | Standardized API Response Envelope | As a Frontend Developer, I want a uniform API success response wrapper, so that client-side payload parsing remains consistent across all endpoints. | Successful API endpoints wrap output payloads in generic `ApiResponse<T>` envelope (`Success`, `Data`, `Message`, `StatusCode`, `TraceId`). xUnit tests verify success payload response formatting. |
| **US-09** | Global Exception Engine & Error Taxonomy | As a System Architect, I want a centralized exception hierarchy mapped to RFC 7807 problem details, so that all errors are handled uniformly system-wide. | .NET 9 `IExceptionHandler` / middleware translates unhandled exceptions into RFC 7807 `ProblemDetails`. Maps explicit exception taxonomy (`DomainException`, `NotFoundException`, `ValidationException`, `UnauthorizedException`, `ForbiddenException`, `ConflictException`) to designated HTTP status codes. Validation failures map field errors into `Errors` dictionary. Unhandled system exceptions return HTTP 500 with masked internal stack traces. xUnit tests verify status codes and RFC 7807 JSON schemas. |

## 3. Detailed User Stories & Implementation Guidance

### US-01: Multi-Tenant Data Isolation Engine

**User Story:** As a System Architect, I want automatic ORM-level tenant isolation enforced on every query and mutation, so that cross-tenant data leakage is structurally impossible.

**Acceptance Criteria:**

- Tenant-scoped domain entities implement `ITenantScopedEntity` exposing a `Guid TenantId`.
- `TenantMiddleware` extracts `tenant_id` from JWT claims or `X-Tenant-Id` request headers to populate a scoped `ITenantContext` service.
- EF Core applies reflection-based `HasQueryFilter(e => e.TenantId == _tenantContext.TenantId)` globally in `OnModelCreating`.
- Auto-populates `TenantId` on newly created entities during `SaveChangesAsync`.
- Integration tests (xUnit) verify that context queries strictly restrict records to the active `TenantId` and block cross-tenant leakage.

### US-02: Identity & Refresh Token Pipeline

**User Story:** As an API Client, I want stateless JWT authentication with HTTP-Only refresh token rotation, so that user sessions remain secure, stateless, and scalable.

**Acceptance Criteria:**

- Configures ASP.NET Core Identity with domain entities `ApplicationUser` (implementing `ITenantScopedEntity`), `ApplicationRole`, and `RefreshToken`.
- JWT service generates access tokens carrying `tenant_id`, `user_id`, `organization_id`, and role claims.
- Exposes typed endpoints: `POST /api/auth/login`, `POST /api/auth/refresh-token`, and `POST /api/auth/revoke-token`.
- xUnit integration tests verify token generation, refresh rotation, and revocation behavior.

### US-03: 9-Tier Additive Claims Authorization Engine

**User Story:** As a Security Lead, I want policy-based authorization mapping our 9 system roles to additive claim bundles, so that granular permission constants govern endpoint access instead of rigid role strings.

**Acceptance Criteria:**

- Maps 9 system roles (SuperAdmin, Property Manager, Board Member, Property Owner, Tenant, Vendor, Committee Member, On-Site Staff, Accountant) to additive claim bundles.
- Registers permission constants (e.g., `Permissions.Ledger.Read`, `Permissions.WorkOrders.Write`) and dynamic authorization policies across API routes.
- Custom authorization handlers explicitly block non-owner renters (Tenant) from financial ledgers, legal notices, and voting routes.
- xUnit tests verify policy evaluation across various claim combinations.

### US-04: Parent Organization Hierarchy & Context Switching

**User Story:** As a Property Manager, I want to switch property contexts within my authorized organization, so that I can manage distinct property entities under one authenticated account.

**Acceptance Criteria:**

- Establishes parent `Organization` entity with `OrganizationId` foreign key mapping on Tenant and ApplicationUser.
- Organization context validation ensures target `TenantId` belongs strictly to the user's authorized `OrganizationId`.
- Exposes endpoints: `GET /api/organization/tenants` and `POST /api/organization/switch-context`.
- `POST /api/organization/switch-context` validates target property membership and issues a newly minted scoped JWT carrying the target `tenant_id` claim, preserving full API statelessness.
- xUnit tests verify cross-tenant organization boundary validation and block unauthorized context switching attempts.

### US-05: Security Hardening & Rate Limiting

**User Story:** As a System Administrator, I want brute-force lockout policies and sliding-window rate limiting, so that the API is hardened against abuse.

**Acceptance Criteria:**

- Configures `IdentityOptions` brute-force lockout (5 failed attempts = 15-minute lock).
- Configures `Microsoft.AspNetCore.RateLimiting` sliding window limits (5 requests/minute per client IP) on `/api/auth/*` routes, returning HTTP 429 when exceeded.
- Enforces Zero Raw Financial Data Policy (all ACH/payment details tokenized via external processor).
- xUnit tests verify lockout triggers and HTTP 429 rate limit enforcement.

### US-06: Presigned Object Storage Service

**User Story:** As an API User, I want to upload document attachments directly to object storage via presigned URLs, so that file transfers bypass Web API server memory.

**Acceptance Criteria:**

- Creates a `DocumentAttachment` EF Core entity tracking upload metadata (`Id`, `TenantId`, `S3Key`, `FileName`, `ContentType`, `ByteSize`, `UploadedByUserId`).
- `IS3StorageService` generates 15-minute presigned PUT URLs scoped to `{TenantId}/{Category}/{EntityId}/{Guid}.ext`.
- Validates allowed MIME types (JPEG, PNG, WebP, PDF) and enforces a hard 10MB byte size limit before signing.
- Exposes endpoint: `POST /api/documents/presign-upload`.
- xUnit tests verify presigned key path generation and 10MB ceiling enforcement.

### US-07: Audit Logging & Soft Delete Interceptor

**User Story:** As a Compliance Officer, I want automated soft-deletes and entity change auditing, so that deleted data is recoverable and state modifications are historically traceable.

**Acceptance Criteria:**

- Entities implement `IAuditableEntity` or `ISoftDelete` contracts, backed by an `AuditLog` entity.
- Custom EF Core `SaveChangesInterceptor` captures entity state changes, original vs. updated JSON values (via `System.Text.Json`), user identity, and timestamps synchronously during `SaveChangesAsync` within the primary database transaction.
- Global query filter applies `IsDeleted == false` automatically.
- xUnit tests verify soft-deleted items are excluded from normal queries and audit entries persist automatically on entity modifications.

### US-08: Standardized API Response Envelope

**User Story:** As a Frontend Developer, I want a uniform API success response wrapper, so that client-side payload parsing remains consistent across all endpoints.

**Acceptance Criteria:**

- All successful API endpoints wrap output payloads in a generic `ApiResponse<T>` envelope containing `Success`, `Data`, `Message`, `StatusCode`, and `TraceId`.
- xUnit integration tests verify success payload response formatting across endpoints.

### US-09: Global Exception Handling Engine & Error Taxonomy

**User Story:** As a System Architect, I want a centralized C# exception hierarchy mapped directly to an RFC 7807 error middleware, so that all domain, validation, and infrastructure errors are handled identically system-wide.

**Acceptance Criteria:**

- Implements .NET 9 `IExceptionHandler` / middleware that intercepts all unhandled and domain exceptions system-wide.
- Maps explicit exception taxonomy to designated HTTP status codes:
  - `DomainException` → HTTP 400 Bad Request / HTTP 422 Unprocessable Entity
  - `NotFoundException` → HTTP 404 Not Found
  - `ValidationException` → HTTP 400 Bad Request
  - `UnauthorizedException` → HTTP 401 Unauthorized
  - `ForbiddenException` → HTTP 403 Forbidden
  - `ConflictException` → HTTP 409 Conflict
- Translates all custom exceptions into RFC 7807 `ProblemDetails` payloads containing `Type`, `Title`, `Status`, `Detail`, `Instance`, `Errors` dictionary, and `TraceId`.
- Validation failures automatically map field-level validation errors directly into the `Errors` dictionary using standardized error codes.
- Unhandled system exceptions return HTTP 500 Internal Server Error, masking internal stack traces in non-Development environments.
- xUnit tests verify that throwing each custom exception type returns the exact designated HTTP status code and RFC 7807 JSON schema.
