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

### US-19: Unified Single Property & Unit Form Editor

**As a** Property Manager, **I want** a single unified form to create or update a property
and its child units, **so that** physical assets can be configured without navigating
duplicate wizard screens.

- **Primary Role:** Property Manager (`Permissions.Property.Manage`).
- **Authorized Secondary Roles:** None named in the story — see the Executive Summary's
  least-privilege note.
- **Prohibited Roles:** Tenant, Vendor (denied both at the route via `denyRolesGuard` and at
  the API via the `Permissions.Property.Manage`/`Read` policies).
- **Required Permission Claims:** `Permissions.Property.Manage` (create/update),
  `Permissions.Property.Read` (fetch for edit mode).

**What shipped:**
- `PropertiesController` (`src/Ten21.Api/Controllers/PropertiesController.cs`) replaces the
  US-01 throwaway proof-of-concept with real `GET /api/properties/{id}`,
  `POST /api/properties`, and `PUT /api/properties/{id}` actions. `GET /api/properties`
  (list, no pagination yet) stays temporarily minimal — US-20 owns turning it into the real
  paginated/searchable endpoint.
- `Property` gained `Name`, `PropertyType`, `StreetAddress2`, `Country`, `DefaultTargetRent`,
  and a `Units` collection; `StreetAddress`/`StateProvince` were renamed to
  `StreetAddress1`/`State` (migration `AddPropertyUnitSprint3Fields` uses `RenameColumn`, not
  drop/recreate, so no data loss). `Unit` is a brand-new tenant-scoped entity (see Executive
  Summary).
- `DefaultTargetRent` cascades to a new unit's `TargetRent` only when the unit doesn't
  specify its own value (`unitRequest.TargetRent ?? request.DefaultTargetRent`) — a one-time
  default applied server-side at create/add time, not a live formula recalculated later.
- **Real bug found and fixed during testing, not by inspection**: adding a brand-new `Unit`
  to an already-tracked `Property`'s `Units` navigation collection
  (`property.Units.Add(new Unit {...})`) inside `UpdateProperty` — mixed in the same
  `SaveChanges` call as an edited sibling unit and a removed one — left that new `Unit`'s
  entry out of `Ten21DbContext.ApplyTenantStamping()`'s pass entirely, so it never got its
  `TenantId` stamped and failed the tenant-ownership check. Fixed by adding new units
  explicitly via `_dbContext.Units.Add(...)` with `PropertyId` set directly, rather than
  relying on navigation-collection fixup, in the update path (`CreateProperty`'s all-new
  graph doesn't hit this — only the update path's mixed Added/Modified/Deleted batch does).
  If touching `Unit` reconciliation logic again, prefer explicit `DbSet.Add()` over
  navigation-collection `.Add()` for anything sharing a `SaveChanges` call with edits to
  sibling entities.
- Server-side sanitization (`IInputSanitizer`/`HtmlInputSanitizer`, Executive Summary) is
  applied to every free-text field before persistence in both create and update.
- Frontend: `PropertyFormContainer` (`/properties/new`, `/properties/:id`) with
  `PropertyInfoForm` and `UnitListEditor` sub-components, typed Angular reactive forms
  (`property-form.types.ts` — a bare `FormGroup`/`FormArray` type falls back to an
  index-signature `controls` object that the project's `noPropertyAccessFromIndexSignature`
  TypeScript setting then rejects for every `form.controls.name`-style template access).
  Save persists then navigates to `/properties` (not yet a real route until US-20 lands —
  falls through to the wildcard `dashboard` redirect in the meantime) and shows a toast via a
  new small app-wide `ToastService`/`ToastHost` (the first feature needing one). Apply
  persists and stays on the page, converting the route to `/properties/:id` via
  `router.navigate(..., { replaceUrl: true })`. Cancel and any other navigation away from a
  dirty form go through a new generic `unsavedChangesGuard` (`CanDeactivateFn`) — generic
  rather than component-specific so a future form page can reuse it.

**Deliberately deferred to US-20:** the `/properties` list page itself, pagination, search,
and the occupancy-status badge styling.

### US-20: Property & Unit List View with Real-Time Search & Pagination

**As a** Property Manager, **I want** an explicit, searchable portfolio list displaying
properties and individual unit/suite numbers, **so that** I can monitor occupancy and asset
health across my portfolio.

- **Primary Role:** Property Manager (`Permissions.Property.Read`).
- **Authorized Secondary Roles:** None named in the story.
- **Prohibited Roles:** Tenant only — unlike US-19/21/22, Vendor is **not** prohibited here
  (the story's own acceptance criteria say "Non-owner Tenants (Tenant)" and stop there); the
  `/properties` route and `Permissions.Property.Read` policy reflect that distinction.
- **Required Permission Claims:** `Permissions.Property.Read`.

**What shipped:**
- `GET /api/properties` was rebuilt from a broken, untested placeholder (see below) into the
  real endpoint: `PropertyListItemDto`/`PropertyListResponse`
  (`src/Ten21.Api/Contracts/Properties/PropertyContracts.cs`) group each property with its
  nested, non-deleted units, ordered by name. `pageNumber`/`pageSize` are both optional
  query parameters — supplying `pageSize` does real server-side `Skip`/`Take` and
  `TotalCount` always reports the total *property* count (matching the "Showing 1-15 of 42
  properties" wording), but the Angular list page itself calls the endpoint with neither
  parameter and fetches the whole portfolio in one request, since a debounced client-side
  search can't filter rows outside whatever page a server-paginated query happened to
  return. Pagination, search, and the PropertyType filter badges are therefore all
  implemented client-side (`PropertyList`, `computed()` signals) over that one in-memory
  set — appropriate for a landlord's own portfolio size, not a dataset that needs query
  pushdown.
- **Real, pre-existing bug found and fixed while rebuilding this action**: the previous
  `GET /api/properties` (written during US-19, never covered by a test for the list action
  specifically) did `.Include(p => p.Units).Select(p => ToResponse(p))` directly on an
  `IQueryable<Property>` — calling a C# method inside a LINQ-to-Entities `Select` that EF
  Core can't translate to SQL. It would have thrown `InvalidOperationException` the first
  time anything actually called this endpoint. Fixed by materializing the query
  (`ToListAsync()`) before mapping client-side, and finally covered by
  `GetProperties_ReturnsOnlyActiveTenantsProperties_WithNestedUnits` /
  `GetProperties_WithPageSize_PaginatesAndReportsTotalPropertyCount` in
  `PropertiesControllerTests`. Lesson for future controller work in this codebase: don't call
  a private/static C# method inside `.Select()` on an `IQueryable` — either translate it to
  an expression EF can push down, or `ToListAsync()` first and map in memory.
- Occupancy badges: Vacant = Alert Amber (`bg-amber/10 text-amber`), Occupied = Financial
  Emerald (`bg-emerald/10 text-emerald`), Maintenance = Rose (`bg-rose/10 text-rose` — not
  specified by the acceptance criteria, chosen because `DESIGN_SYSTEM.md` §2 itself defines
  Rose for "urgent maintenance notices").
- Empty-state onboarding card ("Add First Property" / "Download Sample Spreadsheet
  Template") only shows when the workspace has zero properties at all; a distinct "no
  results" message shows when search/filters narrow an otherwise non-empty portfolio down to
  nothing, so the two states aren't conflated. The sample template
  (`frontend/public/assets/templates/property-import-template.csv`) is a small static asset
  with the exact column headers US-21's importer is specified to expect
  (`PropertyName,PropertyType,StreetAddress1,City,State,PostalCode,Country,UnitIdentifier,TargetRent`)
  — added now so the link isn't dead before US-21 exists, and forward-compatible with it.
- Each property card links to `/properties/:id` (Edit) — without this, US-19's edit route
  would have been unreachable from anywhere in the app now that the dashboard's "Add
  Property" shortcut was replaced with a "Properties" link to this list page.
