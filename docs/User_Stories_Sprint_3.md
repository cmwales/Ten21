# Ten21 - Sprint 3 Property & Unit Setup User Stories

This document covers **Sprint 3: Property & Unit Setup** (`ROADMAP.md`) — the first real
Property/Unit CRUD surface in the codebase, replacing the throwaway `PropertiesController`
proof-of-concept that only ever existed to demonstrate US-01's tenant-isolation engine.
Follows the same format as `User_Stories_Phase_5.md`. Per `FEATURES.md`'s Feature
Specification Standards, every story below declares its Primary Role, Authorized Secondary
Roles, Prohibited Roles, and Required Permission Claims.

## 1. Executive Summary & Core Design Directives

- **`Property` graduates from proof-of-concept to real entity.** Its own class comment said
  "Full property/unit modeling is Phase 3 (DATA_MODEL) work and will expand this
  significantly" — this sprint is that expansion. `StreetAddress`/`StateProvince` are
  renamed to `StreetAddress1`/`State` and joined by `StreetAddress2`, `Country`, `Name`,
  `PropertyType`, and `DefaultTargetRent`, per this sprint's acceptance criteria. This is a
  breaking schema change made freely pre-release (nothing in production yet).
- **`Unit` is a brand-new tenant-scoped entity**, not a value object hanging off `Property`.
  It carries its own `TenantId` (defense-in-depth, same as every other tenant-scoped entity
  — see US-01), a `PropertyId` foreign key, `UnitIdentifier`, `TargetRent`, and
  `OccupancyStatus`. `DefaultTargetRent` on `Property` cascades to new units at creation time
  only (a one-time default, not a live formula) — a unit's own `TargetRent` is what's
  authoritative afterward.
- **Only `PropertyManager` gets the new `Permissions.Property.*` claims** (plus
  `SuperAdmin`, which inherits every permission automatically via `Permissions.All`). None of
  these four stories names an authorized secondary role — only a primary role and prohibited
  roles — so, per the principle of least privilege, no other role bundle changes.
- **US-22's payment check is a deliberate placeholder, not a real integration.** Phase 1
  (Monetization & Billing Logic) — the phase that would define a real payment
  ledger — is still `PENDING (NEXT UP)` in `ROADMAP.md`, and no such table exists anywhere in
  the codebase yet. Rather than inventing Phase 1's schema early (out of this sprint's
  scope) or skipping the story, `HasAppliedPayments(propertyId)` is implemented as a single
  method that always returns `false` today, with every hard-delete/soft-delete branch of
  `DELETE /api/properties/:id` built exactly as specified around that call. When Phase 1
  ships a real payment ledger, swapping this one method's body to a genuine query is the only
  change needed — the deletion branching logic itself needs no rework. This was an explicit,
  confirmed choice, not an assumption.
- **Server-side sanitization uses a real HTML sanitizer, not encoding.** US-19 and US-21 both
  require stripping HTML/script content from free-text fields before persistence. `Ganss.Xss`
  (`HtmlSanitizer`) is added for this — it strips tags/attributes rather than just escaping
  them, appropriate for plain-text fields that should never contain markup at all.
- **CSV/XLSX parsing uses `CsvHelper` + `ClosedXML`**, both MIT-licensed and file-format-only
  (no cloud/commercial dependency), fitting the "ultra-low operating cost" mandate. Neither
  was previously referenced anywhere in the solution.
- **The bulk importer never touches disk or object storage.** Per US-21's acceptance
  criteria, the uploaded file is parsed directly from the `IFormFile`'s in-memory stream and
  the buffer is discarded once the request completes — no temp file, no S3/R2 presigned
  upload, unlike `DocumentAttachment`'s photo-vault flow (US-06).
- **Branch-per-story, same cadence as Phase 5.** Each story below is built on its own branch
  and merged into `main` with its own passing build/tests before the next branch is cut.

## 2. User Story Summary Matrix

| ID | Story Title | User Story Statement | Core Acceptance Criteria |
| --- | --- | --- | --- |
| **US-19** | Unified Single Property & Unit Form Editor | As a Property Manager, I want a single unified form to create or update a property and its child units, so that physical assets can be configured without navigating duplicate wizard screens. | `PropertyFormContainer` (Angular) serves `/properties/new` and `/properties/:id` via `PropertyInfoForm`/`UnitListEditor` sub-components; captures Name, PropertyType, broken-out address fields, and per-unit `UnitIdentifier`/`TargetRent`/`OccupancyStatus`; `DefaultTargetRent` cascades to new units with per-unit override; Save/Apply/Cancel toolbar with a `CanDeactivate` guard; all string inputs sanitized server-side before persistence. |
| **US-20** | Property & Unit List View with Real-Time Search & Pagination | As a Property Manager, I want an explicit, searchable portfolio list displaying properties and individual unit/suite numbers, so that I can monitor occupancy and asset health across my portfolio. | `GET /api/properties` projects a lightweight `PropertyListDto` (with child unit rows) and supports `pageNumber`/`pageSize`; Angular list shows an Occupancy badge (Vacant = Alert Amber, Occupied = Financial Emerald), client-side pagination (15/30/50 per page, default 15) with a "Showing X–Y of Z" counter, debounced search across name/address/zip/unit#, PropertyType filter badges, and an empty-state onboarding card. |
| **US-21** | In-Memory Bulk CSV/XLSX Property & Unit Importer | As a Property Manager, I want to drag and drop a .csv or .xlsx spreadsheet, so that I can onboard large property and unit inventories in under two minutes without manual entry. | `/properties/import` dropzone (10MB limit, .csv/.xlsx); file streamed directly from request memory via `CsvHelper`/`ClosedXML`, never written to disk or object storage; formula-injection sanitization (`'` prefix on cells starting with `= + - @`); preview grid mapping parsed columns; row-level validation errors in `ApiResponse<T>` (e.g. "Row 42: Target Rent must be a positive number"); insertion wrapped in one EF Core transaction with full rollback on any row failure. |
| **US-22** | Conditional Payment-Based Property Deletion | As a System Architect, I want property deletion requests evaluated against financial transaction history, so that accidental setups can be purged while preserving audit and financial compliance. | `DELETE /api/properties/:id` checks for applied payments (placeholder — always `false` until Phase 1's ledger exists, see Executive Summary); zero payments → hard delete (`DbContext.Properties.Remove()`); applied payments → soft delete (`IsDeleted = true`) excluded via the existing global query filter; `AuditSaveChangesInterceptor` cascades `IsDeleted = true` to child `Unit` rows on a Property soft-delete. |

## 3. Detailed User Stories & Implementation Guidance

_Filled in per story as each branch lands — see the Executive Summary above for the
cross-cutting decisions that apply to all four._
