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

### US-21: In-Memory Bulk CSV/XLSX Property & Unit Importer

**As a** Property Manager, **I want** to drag and drop a .csv or .xlsx spreadsheet, **so
that** I can onboard large property and unit inventories in under two minutes without
manual entry.

- **Primary Role:** Property Manager (`Permissions.Property.Import`).
- **Authorized Secondary Roles:** None named in the story.
- **Prohibited Roles:** Tenant, Vendor.
- **Required Permission Claims:** `Permissions.Property.Import`.

**What shipped:**
- **One request does everything — parse, sanitize, validate, and (if every row passes)
  commit.** There's no separate "preview" call before the real import: `POST
  /api/properties/import` always returns every parsed row (`ImportRowResult`) alongside its
  validation outcome in the same response the Angular preview grid renders, whether the
  batch succeeded or not. This avoids re-uploading the file twice and matches the acceptance
  criteria's own framing — "preview grid" and "row-level validation errors" both come from
  one `ApiResponse<T>`-wrapped body, `ImportPropertiesResponse`.
  (`src/Ten21.Api/Contracts/Properties/PropertyImportContracts.cs`)
- **Nothing is added to the `DbContext` until every row has passed validation.** A single
  invalid row rejects the entire file (`Success: false`, `PropertiesCreated`/`UnitsCreated`
  stay 0) — even rows that were individually valid are not partially committed. Only once
  every row passes does `PersistImportedRowsAsync` open an **explicit**
  `Database.BeginTransactionAsync()`/`CommitAsync()` around the insert — literal, per the
  acceptance criteria's own wording, even though a single `SaveChangesAsync()` call is
  already atomic on its own; the explicit transaction is what protects against a DB-level
  failure (e.g. a constraint violation) that only surfaces *after* every row already passed
  application-level validation.
- **Parsing is header-name-based, not position-based**, for both `.csv` (`CsvHelper`) and
  `.xlsx` (`ClosedXML`) — column order in the uploaded file doesn't matter as long as the
  header row has all 9 expected names (`PropertyImportFileParser`,
  `IPropertyImportFileParser`/`RawImportRow` in `Ten21.Application.Abstractions`). A file
  missing a required header is rejected up front as a whole-file `ValidationException`
  (400), distinct from a row-level validation failure. `RowNumber` matches what a user sees
  opening the file in Excel/Sheets (header = row 1, first data row = row 2), for messages
  like "Row 42: Target Rent must be a positive number."
- **Rows are grouped into properties by an exact match on (Name, StreetAddress1, City,
  State, PostalCode, Country)** — multiple spreadsheet rows sharing all six sanitized values
  become one `Property` with multiple child `Unit`s, exactly as the sample template
  (`frontend/public/assets/templates/property-import-template.csv`, added during US-20) is
  shaped. Newly imported units default to `OccupancyStatus.Vacant` — the spreadsheet has no
  occupancy column.
- **Two independent sanitization passes on every text cell, layered**: the existing
  `IInputSanitizer` (HTML/XSS stripping, US-19) runs first, then the new
  `FormulaInjectionGuard.Sanitize` (`Ten21.Domain.Common` — pure, dependency-free, unlike
  `IInputSanitizer` which wraps an external library) prepends a `'` to any value starting
  with `= + - @`, defending against CSV/formula injection if this data is ever re-exported
  to a spreadsheet later. US-21's acceptance criteria only mandated the formula-injection
  half; the HTML pass was kept for consistency with every other write path into `Property`/
  `Unit` in this codebase, not because the story asked for it twice.
- `TargetRent` validation for imported rows is stricter than the interactive US-19 form: it
  must be a **positive** number if provided (`<= 0` is rejected, not just negative) — the
  acceptance criteria's own example message ("Target Rent must be a positive number") is
  reused verbatim, and a blank cell is treated as no override (falls back to nothing, since
  bulk-imported properties have no `DefaultTargetRent` field to cascade from).
- Frontend: `PropertyImport` (`/properties/import`) — a dropzone (drag-and-drop + a hidden
  file input for click-to-browse) with client-side extension/size pre-checks (matching the
  server's own 10MB/`.csv`/`.xlsx` limits, so an obviously-invalid file never gets uploaded
  at all) before calling `PropertyService.importProperties()` (multipart `FormData`). The
  same response renders both the summary banner (success/failure counts) and the full
  preview table, with invalid rows highlighted and their error text shown inline — one
  round trip, one render, matching the backend design above. Linked from both the property
  list's toolbar and its own empty-state card's "Download Sample Spreadsheet Template" link
  (added in US-20, now actually consumed here).

### US-22: Conditional Payment-Based Property Deletion

**As a** System Architect, **I want** property deletion requests evaluated against
financial transaction history, **so that** accidental setups can be purged while preserving
audit and financial compliance.

- **Primary Role:** Property Manager (`Permissions.Property.Delete`).
- **Authorized Secondary Roles:** None named in the story.
- **Prohibited Roles:** Tenant, Vendor.
- **Required Permission Claims:** `Permissions.Property.Delete`.
- **No frontend component for this story.** Unlike US-19/20/21, the acceptance criteria for
  US-22 describe only a backend endpoint — no Angular page or button is specified, and none
  was added. Deliberately: exposing a delete action in the UI ahead of `HasAppliedPaymentsAsync`
  becoming a real query (see below) would let a user trigger what looks like a safe,
  conditional delete but is actually unconditionally a hard delete today. A UI entry point
  belongs with — or after — Phase 1's payment ledger, not before it.

**What shipped:**
- `DELETE /api/properties/{id}` — `HasAppliedPaymentsAsync` is the placeholder described in
  the Executive Summary (always `false` until Phase 1 ships a real payment ledger), so every
  delete today takes the hard-delete branch. The branching logic itself is built exactly to
  spec: zero payments → hard delete; applied payments → soft delete
  (`IsDeleted = true`, excluded via the existing global query filter). Swapping
  `HasAppliedPaymentsAsync`'s body for the genuine
  `PaymentLedger.AnyAsync(x => x.PropertyId == id && x.AmountPaid > 0)` query is the only
  change needed when that ledger exists.
- **Real bug found and fixed while building this, not by inspection**: the first version of
  this feature tried to cascade `IsDeleted = true` from `Property` to its `Unit`s *inside*
  `AuditSaveChangesInterceptor`, triggered by seeing the `Property` transition to
  `EntityState.Deleted`. That doesn't work — EF Core's own relationship-severance check
  throws `InvalidOperationException` **synchronously, inside `Remove()`**, before
  `SaveChanges` (and this interceptor) ever runs, if a parent is marked `Deleted` while an
  already-tracked child with a required, `DeleteBehavior.Restrict` foreign key
  (`Unit.PropertyId`, see `UnitConfiguration`) is left `Unchanged`. The fix: both branches of
  `DeleteProperty` now `Remove()` the `Property` *and* every one of its `Unit`s together, in
  the same call — `AuditSaveChangesInterceptor` then converts each `Deleted` entry it's
  given independently (no `Property`-specific cascade code needed there at all). This was
  caught by `SoftDelete_OfPropertyAndItsUnitsTogether_ConvertsBothToSoftDelete`
  (`AuditSaveChangesInterceptorTests`) — worth remembering for any future entity with a
  required, `Restrict` foreign key to a soft-deletable parent: the parent and child must be
  `Remove()`-d together, not the parent alone with cascade logic deferred to a
  `SaveChangesInterceptor`.
- **New mechanism: `IHardDeleteOverride`** (`Ten21.Application.Abstractions`, implemented in
  `Ten21.Infrastructure.Persistence.HardDeleteOverride`, scoped like `ITenantContext`) — an
  explicit, per-request, per-entity-*instance* (reference equality, not `Id` equality)
  opt-out from `AuditSaveChangesInterceptor`'s default soft-delete conversion. Before this
  story, every `Remove()` call in the codebase was unconditionally converted to a soft
  delete; US-22 needed a real hard-delete path for the first time, and this is deliberately
  an explicit, narrow, per-call-site opt-in (via `MarkForHardDelete`) rather than a global
  setting, so soft-delete stays the safe, silent default everywhere else.
