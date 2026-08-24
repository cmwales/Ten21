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
