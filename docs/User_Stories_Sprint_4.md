# Ten21 - Sprint 4 Resident Onboarding User Stories

Bridges physical property/unit inventory (Sprint 3) with resident identity, ahead of formal
lease setup (Sprint 5). Follows the same format as `User_Stories_Sprint_3.md`. Per
`FEATURES.md`'s Feature Specification Standards, every story below declares its Primary
Role, Authorized Secondary Roles, Prohibited Roles, and Required Permission Claims.

## 1. Executive Summary & Core Design Directives

- **`ResidentProfile` is a brand-new entity, deliberately NOT a repurposed
  `TenantMembership`.** `TenantMembership` already means "this login has this role in this
  multi-tenancy partition" (`TenantId, UserId, RoleId, IsPrimary`, no `PropertyId`,
  deliberately not soft-deletable). An occupant of a specific unit is a different
  relationship (person -> leasable Property row, with move-in/move-out history worth
  keeping), so it gets its own entity. `TenantMembership` is still created (unchanged) for
  any occupant who ends up with a login -- `ResidentProfile.UserId` links to it once
  provisioned.
- **Every occupant with an email gets provisioned a login, not just the primary resident.**
  Confirmed explicitly with the founder (this reverses this doc's own first draft, which
  defaulted to primary-only). Primary and secondary occupants are otherwise structurally
  identical `ResidentProfile` rows, distinguished only by `OccupantType`.
- **Emergency contacts are a one-to-many `EmergencyContact` entity, not fields on
  `ResidentProfile`.** Also confirmed explicitly, reversing this doc's first draft. A
  resident can have more than one emergency contact.
- **US-24's "Zero-Token" provisioning reuses existing interim-token infrastructure, not new
  machinery.** `IJwtTokenService.GenerateInterimAccessToken(userId, purpose)` (built for
  US-15's profile-incomplete gate) already does exactly what a "must change password before
  anything else" gate needs -- a short-lived, tenant-less, role-less token carrying only a
  `purpose` claim. A new `TokenPurposes.PasswordChangePending` constant is the only new
  primitive required; no new token-generation method, no code-hash/expiry claims like
  US-17's 2FA challenge needed (there's no "code" here, just a boolean gate).
- **SMTP stays on `ConsoleEmailSender` for this sprint.** US-24's welcome email is built
  against the existing `IEmailSender` abstraction like every other email in this codebase
  (US-14/US-16/US-17) -- confirmed explicitly that configuring real Gmail SMTP credentials
  is a later, separate step, not part of this sprint.
- **US-25's "block Tenant from financial ledgers / property settings" is largely already
  true by construction, not new work.** `RolePermissions.Bundles[RoleNames.Tenant]` has
  never granted any `Permissions.Property.*` or `Permissions.Ledger.Write` claim (Tenant's
  bundle is `WorkOrders.Write, Announcements.Read` only) -- every `PropertiesController`
  action already 403s a Tenant-role caller today. US-25's genuinely new surface is the
  dual-consent community directory endpoint (`Property.AllowTenantDirectory AND
  ResidentProfile.ShowInDirectory`), plus a regression test proving the existing hard-block
  still holds now that Tenant-role logins actually exist (US-24) to test it with.
- **Branch-per-story, same cadence as Sprint 3/Phase 5.** US-23 -> US-24 -> US-25, each on
  its own branch, merged into `main` with its own passing build/tests before the next
  branch is cut.

## 2. User Story Summary Matrix

| ID | Story Title | User Story Statement | Core Acceptance Criteria |
| --- | --- | --- | --- |
| **US-23** | Tenant Profile Directory | As a Property Manager, I want to capture primary and secondary occupant details, emergency contacts, and departure logistics, so I can maintain a resident directory and populate the property detail drawer. | `ResidentProfile` (PropertyId, OccupantType, contact fields, `ForwardingAddress`, `NoticeGivenDate`, `ShowInDirectory` default false) with one-to-many `EmergencyContact` rows; slide-out Tenant Quick-View Drawer on `/properties/:id`. |
| **US-24** | Zero-Token Tenant Welcome & Provisioning | As a Property Manager, I want tenant emails recorded so the system provisions credentials and sends a welcome notification directing residents to log in at the main portal URL. | Any `ResidentProfile` with an email provisions an `ApplicationUser` + `TenantMembership` (Tenant role) with an auto-generated temp password and `MustChangePassword = true`; welcome email via `IEmailSender` links to `https://app.ten21.io/login`; login is gated on a forced password change before a real session issues. |
| **US-25** | Tenant Access & Directory Privacy | As a Security Lead, I want policy-layer authorization rules enforcing tenant data isolation and directory privacy, so non-owner renters cannot access unauthorized property data. | Confirms the existing `Permissions.Property.*` hard-block against Tenant-role callers (regression test); new dual-consent directory endpoint requiring both `Property.AllowTenantDirectory` and `ResidentProfile.ShowInDirectory`. |

## 3. Detailed User Stories & Implementation Guidance

_Filled in per story as each branch lands -- see the Executive Summary above for the
cross-cutting decisions that apply to all three._

### US-23: Tenant Profile Directory

**As a** Property Manager, **I want** to capture primary and secondary occupant details,
emergency contacts, and departure logistics, **so that** I can maintain a resident directory
and populate the property detail drawer.

- **Primary Role:** Property Manager (`Permissions.Resident.Manage`/`Permissions.Resident.Read`).
- **Authorized Secondary Roles:** None named in the story -- same least-privilege reasoning
  as every Sprint 3 property-setup story.
- **Prohibited Roles:** Tenant, Vendor (Tenant's own permission bundle has never included
  any `Permissions.Resident.*` claim, so this is enforced by omission, not an explicit deny
  rule -- consistent with how `Permissions.Property.*` already worked before this sprint).
- **Required Permission Claims:** `Permissions.Resident.Manage` (create/update/delete),
  `Permissions.Resident.Read` (list/get).

**What shipped:**
- `ResidentProfile` (`src/Ten21.Domain/Entities/ResidentProfile.cs`) -- a brand-new entity,
  not a repurposed `TenantMembership` (see Executive Summary). `PropertyId` (required,
  `DeleteBehavior.Restrict` -- deleting a Property with resident rows fails loudly rather
  than silently orphaning them), `UserId` (nullable, populated by US-24's provisioning),
  `OccupantType` (Primary/Secondary), contact fields, `ForwardingAddress`,
  `NoticeGivenDate`, `ShowInDirectory` (default false). `ISoftDelete` -- unlike
  `TenantMembership`, occupancy history is worth keeping.
- `EmergencyContact` (`src/Ten21.Domain/Entities/EmergencyContact.cs`) -- one-to-many off
  `ResidentProfile` (`DeleteBehavior.Cascade`), NOT `ISoftDelete` (same reasoning as
  `TenantMembership`: contact metadata with no independent audit need -- removing one is a
  genuine delete).
- `ResidentsController` (`src/Ten21.Api/Controllers/ResidentsController.cs`) -- nested under
  `api/properties/{propertyId}/residents`; every action re-scopes by `PropertyId ==
  propertyId` (not just a bare `{id}` lookup) per CLAUDE.md's BOLA/IDOR mandate. `PUT`
  replaces the full `EmergencyContacts` set (remove-all-then-re-add), matching how the
  drawer's form naturally submits.
- **Real bug found and fixed during testing, not by inspection**: the update path originally
  mutated `resident.EmergencyContacts` navigation (`RemoveRange` + `.Clear()` +
  re-`.Add()`) on an already-tracked, `Include()`-loaded parent. A real xUnit test run
  caught that this produces an unpredictable entity state -- a freshly re-added
  `EmergencyContact` ended up `EntityState.Modified` instead of `Added`, tripping
  `ApplyTenantStamping`'s Modified-state ownership check (it had no `TenantId` yet). Fixed
  by managing `EmergencyContact` rows directly via the `DbSet` (explicit `RemoveRange`/
  `AddRange` against freshly-queried rows) instead of navigation-collection mutation on the
  update path -- the create path's navigation-based approach was unaffected (a brand-new
  parent's navigation adds cascade to `Added` correctly) and was left as-is. Lesson for any
  future "replace a child collection" update path in this codebase: prefer direct `DbSet`
  `RemoveRange`/`AddRange` over `Clear()`+`Add()` on an `Include()`-loaded navigation
  collection.
- Frontend: `ResidentDrawer` (`frontend/src/app/pages/properties/resident-drawer/`) -- the
  slide-out Tenant Quick-View Drawer, triggered by a "View Residents" button on
  `PropertyFormContainer` (edit mode only, since a drawer needs a real `propertyId`).
  Resident list with occupant-type badges, an add/edit form with a dynamic `FormArray` of
  emergency contacts, and `ForwardingAddress` only rendered once `NoticeGivenDate` has a
  value (progressive disclosure, not a hard backend ordering constraint).
- Verified live end-to-end (real browser, real backend): opened the drawer, added a resident
  with one emergency contact, confirmed it appeared with the correct contact count, edited
  it, then deleted it and confirmed the empty state -- all through real HTTP calls, not
  mocked.

**Deliberately deferred to US-24:** login provisioning for a resident with an email --
`ResidentProfile.UserId` stays null for every resident created in this story; the next
branch layers provisioning on top of `CreateResident`.

### US-24: Zero-Token Tenant Welcome & Provisioning

**As a** Property Manager, **I want** tenant emails recorded so the system provisions
credentials and sends a welcome notification directing residents to log in at the main
portal URL, **so that** onboarding a resident needs no separate invitation/token step.

- **Primary Role:** Property Manager (triggers provisioning as a side effect of
  `Permissions.Resident.Manage`, no separate permission claim of its own).
- **Authorized Secondary Roles:** None.
- **Prohibited Roles:** N/A -- this story's actor-facing surface is entirely the resident's
  own login (`/api/auth/login`, `/api/auth/change-temp-password`), open to whichever
  ApplicationUser the challenge token names.
- **Required Permission Claims:** None new -- reuses `Permissions.Resident.Manage`.

**What shipped:**
- `ApplicationUser.MustChangePassword` (`src/Ten21.Infrastructure/Identity/ApplicationUser.cs`) --
  false for every self-registered (US-14) or Google (US-15) account, true only for an
  account `ResidentsController.CreateResident` provisions.
- **Login gate**: `AuthController.Login` checks `MustChangePassword` immediately after the
  password check, *before* resolving tenant membership or checking mandatory 2FA. If true,
  it short-circuits into a `PasswordChangeRequiredResponse` using
  `IJwtTokenService.GenerateInterimAccessToken` with a new `TokenPurposes.PasswordChangePending`
  purpose claim -- reusing US-15's existing interim-token mechanism rather than building a
  parallel one, since there's no "code" to carry as a claim here, just a boolean gate (unlike
  US-17's 2FA challenge, which does need code_hash/code_exp claims).
- **`POST /api/auth/change-temp-password`**: requires a `PasswordChangePending` token
  (checked explicitly, same defensive pattern as `VerifyTwoFactor`). No `CurrentPassword`
  field -- the challenge token itself already proves knowledge of the current (temporary)
  password, so `UserManager.RemovePasswordAsync` + `AddPasswordAsync` replace it directly.
  On success, falls through to the same `CompleteLoginAsync` tail `Login` itself uses
  (resolve membership -> 2FA if the role needs it -> issue a real session) -- extracted as a
  shared private method so both paths stay in sync.
- **Provisioning** (`ResidentsController.ProvisionResidentLoginAsync`, called from
  `CreateResident` whenever `Email` is set): **every occupant with an email is provisioned,
  primary or secondary alike** -- confirmed explicitly with the founder, reversing this doc's
  own first-draft assumption of primary-only. A brand-new email gets a fresh
  `ApplicationUser` (`MustChangePassword = true`, `EmailConfirmed = true` -- the PM entering
  it directly stands in for self-confirmation), a `TenantMembership` (Tenant role,
  `IsPrimary = true`), and a welcome email with the temp password linking to
  `https://app.ten21.io/login` (hardcoded, not the configurable `_frontendBaseUrl` pattern --
  the acceptance criteria's own wording calls for the literal production URL, and unlike
  activation/reset links this one carries no token to be environment-aware about).
- **Cross-PM identity** (raised live by the founder mid-build: "how do we handle rentals made
  by the same person across multiple property managers... we will address that in a future
  sprint"): `ApplicationUser` is already global with no `TenantId` of its own, and
  `TenantMembership` already supports "one login, many tenant/role pairs" by design (see its
  own class comment -- built for PMC staff across a portfolio). `ProvisionResidentLoginAsync`
  checks for an existing `ApplicationUser` by email first; if found, it links that account
  into the new tenant via an additional `TenantMembership` (not `IsPrimary` unless it's their
  first) rather than erroring or creating a duplicate account, and sends a lighter
  "you've been added, log in as usual" email with no password reset involved. Richer cross-PM
  UX (visibility across memberships, consent, conflict resolution) is deliberately deferred,
  per that same conversation.
- `GenerateTemporaryPassword` meets ASP.NET Core Identity's default password policy by
  construction (one guaranteed character from each required category, random-filled, then
  shuffled) and excludes visually ambiguous characters (0/O, 1/I/l) since it's read out of an
  email and typed back in by hand.
- **Frontend gap found and closed, not originally scoped**: the backend mechanism alone left
  a real resident with nowhere to go -- the Angular login page had no handling for
  `PasswordChangeRequiredResponse` at all. Added `AuthService.changeTempPassword()`
  (mirroring `verifyTwoFactor()`'s "use the challenge token directly as the Authorization
  header" pattern) and a new `ChangeTempPassword` page/route
  (`frontend/src/app/pages/change-temp-password/`), matching `VerifyTwoFactor`'s
  "bounce back to /login with no pending challenge" guard.
- Verified live end-to-end in a real browser against the real backend, including the
  frontend piece: a PM added a resident with an email through the real drawer UI, the
  resident logged in through the real `/login` page with the emailed temp password, was
  redirected to `/change-temp-password`, set a new password, landed on `/dashboard` with the
  `Tenant` role -- then confirmed directly that the old temporary password is rejected
  (401) and the new one keeps working (200) on fresh logins.
- 4 new backend unit tests (real `UserManager`/`RoleManager` via a minimal Identity DI stack
  against the same in-memory SQLite connection, not mocked -- this codebase has no mocking
  library and `AuthController` itself is only ever tested this way through full integration
  tests) plus 1 new integration test (`ResidentProvisioningEndToEndTests`) proving the whole
  login -> forced-change -> session flow through the real HTTP pipeline. 3 new frontend
  `AuthService` tests.

### US-25: Tenant Access & Directory Privacy

**As a** Security Lead, **I want** policy-layer authorization rules enforcing tenant data
isolation and directory privacy, **so that** non-owner renters cannot access unauthorized
property data.

- **Primary Role:** Security Lead (policy design), enforced against Tenant (the role being
  restricted) and consumed by Tenant (the directory endpoint's caller).
- **Authorized Secondary Roles:** None.
- **Prohibited Roles:** N/A -- this story restricts Tenant, it doesn't grant a new
  capability to any other role.
- **Required Permission Claims:** `Permissions.Directory.Read` (new, granted to Tenant only
  -- a PM already sees every resident of their own properties unfiltered via
  `Permissions.Resident.Read`; dual-consent privacy is specifically a Tenant-facing
  concern, not a management one).

**What shipped:**
- **The financial-ledger/property-settings hard-block was already true by construction**
  (see the Executive Summary) -- `RolePermissions.Bundles[RoleNames.Tenant]` has never
  granted any `Permissions.Property.*` claim, so every `PropertiesController`/
  `ResidentsController` action already 403s a Tenant-role caller. What US-24 made possible
  for the first time is a *real* Tenant-role session to prove it with, so
  `TenantAccessEndToEndTests` (`tests/Ten21.IntegrationTests`) does exactly that: a
  resident logs all the way in (through the real `MustChangePassword` gate) and gets 403 on
  `GET /api/properties`, `POST /api/properties`, and `GET
  /api/properties/{propertyId}/residents`.
- **New: `Property.AllowTenantDirectory`** (`src/Ten21.Domain/Entities/Property.cs`) --
  defaults false; a PM must opt each property into the community directory explicitly, not
  have it on by default. Exposed on `UpsertPropertyRequest`/`PropertyResponse` (trailing
  parameter with a C# default, so it doesn't force every existing caller to change) and on
  the property edit form as a checkbox.
- **New: `DirectoryController`** (`GET /api/directory`) -- deliberately **not**
  parameterized by `propertyId`. The caller's own occupancy (`ResidentProfile.UserId ==
  their user_id claim`) is what scopes the whole query, so there is no client-suppliable
  property/tenant identifier to tamper with for BOLA purposes; a Tenant simply cannot ask
  "show me the directory for property X." Resolves the caller's occupied Property/Properties,
  finds sibling Property rows sharing the exact same street address with
  `AllowTenantDirectory = true` ("neighboring units" in the flat model, where a suite is its
  own independent Property row rather than a child of a shared parent), and returns
  `DirectoryEntryResponse` (`FirstName`, `LastName`, `UnitIdentifier` only -- never email,
  phone, or emergency contacts) for residents at those siblings with `ShowInDirectory =
  true`, excluding the caller's own entry.
- Verified live end-to-end: a PM created two properties sharing an address (both
  `AllowTenantDirectory = true`), added an opted-in resident to each, one resident logged
  all the way in as Tenant -- confirmed 403 on every property-management endpoint and a
  200 on `/api/directory` containing exactly the sibling's entry (correct name and unit
  identifier), never the caller's own.
- 6 new backend unit tests (`DirectoryControllerTests`) covering the dual-consent matrix
  (both sides opt in / property opts out / resident opts out / different address / no
  profile at all) plus 1 new integration test proving the hard-block-plus-directory flow
  live. 1 frontend assertion strengthened to prove `allowTenantDirectory` actually reaches
  the wire.
