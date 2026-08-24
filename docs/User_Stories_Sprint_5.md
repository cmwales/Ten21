# Ten21 - Sprint 5 Multi-Property Workspace Switching User Stories

Follows the same format as `User_Stories_Sprint_4.md`. Per `FEATURES.md`'s Feature
Specification Standards, every story below declares its Primary Role, Authorized Secondary
Roles, Prohibited Roles, and Required Permission Claims.

## 1. Executive Summary & Core Design Directives

- **The switch-context backend already existed before this sprint.** `OrganizationController`
  (`GET /api/organization/tenants`, `POST /api/organization/switch-context`) was built under
  US-04 and matches this sprint's original spec almost exactly -- it just had no test
  coverage and no frontend consumer. Nothing about its own logic needed to change.
- **The real gap was upstream: nothing could ever grant a user a second `TenantMembership`.**
  Self-registration (US-14) always creates exactly one brand-new `Tenant` for exactly one
  user; no `Organization` row has ever been created by any code path. Sprint 5's actual new
  backend work (US-26) is the missing source of multi-tenant data, not the switch itself.
- **Cross-PM resident linking (US-24) already produces a real second `TenantMembership`,
  with zero further backend changes needed.** `SwitchContext`'s `OrganizationId` boundary
  check (`_tenantContext.OrganizationId is { } currentOrgId && targetTenant.OrganizationId
  != currentOrgId`) only fires when the CALLER'S CURRENT token already carries a non-null
  organization -- true for nobody today, since Organizations don't exist yet outside this
  sprint's own new flow. A resident with `TenantMembership` rows in two unrelated PMs'
  tenants can already switch between them through the existing endpoint. US-27 proves this
  live rather than building anything new for it.
- **No new permission claim.** The existing implementation treats "owns a `TenantMembership`
  row for the target tenant" as sufficient authorization on its own -- confirmed explicitly
  with the founder as the right call, not a gap. A `Permissions.Org.Switch`-style claim
  would be redundant with that check, not defense-in-depth.
- **US-28 is building the app's first top-nav shell, not just adding a dropdown to one.**
  `frontend/src/app/app.ts` was bare (`RouterOutlet` + `ToastHost` only) going into this
  sprint -- no header/nav component existed anywhere in the frontend.
- **Branch-per-story, same cadence as every prior sprint.** US-26 -> US-27 -> US-28, each on
  its own branch, merged into `main` with its own passing build/tests before the next branch
  is cut.

## 2. User Story Summary Matrix

| ID | Story Title | User Story Statement | Core Acceptance Criteria |
| --- | --- | --- | --- |
| **US-26** | Portfolio Expansion | As a Property Manager, I want to add another property/workspace under my own portfolio, so that I have more than one Tenant to actually switch between. | `POST /api/organization/workspaces` creates a new `Tenant`, establishing (or reusing) an `Organization` to parent it and the caller's current tenant, and grants the caller PropertyManager+PropertyOwner membership on it. |
| **US-27** | Switch-Context Test Coverage | As a Security Lead, I want the existing context-switch endpoint proven correct under real conditions, so that a feature this security-sensitive isn't running on faith. | Unit + integration tests: a PM switching between two owned workspaces, a 403 on an unauthorized target tenant, and the cross-PM resident scenario switching successfully with no backend changes. |
| **US-28** | Workspace Switcher UI | As a Multi-Property Resident or PMC Manager, I want to switch my active property workspace via a top-navigation dropdown, so that I can access different property portals under a single login identity without re-authenticating. | A new top-nav header component (first in the app) renders the active workspace with a dropdown of every authorized workspace; selecting one calls the existing switch-context endpoint and reactively refreshes route data without a reload. |

## 3. Detailed User Stories & Implementation Guidance

_Filled in per story as each branch lands -- see the Executive Summary above for the
cross-cutting decisions that apply to all three._

### US-26: Portfolio Expansion

**As a** Property Manager, **I want** to add another property/workspace under my own
portfolio, **so that** I have more than one Tenant to actually switch between.

- **Primary Role:** Property Manager -- checked directly against the specific role on the
  caller's *current* tenant, not just "any membership exists," since only the operator
  (not e.g. a Board Member on that same tenant) may expand the portfolio.
- **Authorized Secondary Roles:** None.
- **Prohibited Roles:** N/A -- gated by role-on-current-tenant, not a blanket role deny.
- **Required Permission Claims:** None new -- same membership/role-ownership check every
  other tenant-scoped write in this codebase already uses, no separate permission claim
  layered on top (mirrors the founder's own confirmed call on US-27's switch-context
  authorization).

**What shipped:**
- `POST /api/organization/workspaces` (`OrganizationController.AddWorkspace`) -- creates a
  new `Tenant`, establishing (or reusing) an `Organization` to parent it and the caller's
  current tenant. First expansion promotes the caller's existing standalone tenant into an
  `Organization`'s first member retroactively (nothing ever created one before this story);
  every subsequent expansion from that same tenant reuses the same `Organization`. Grants
  the caller PropertyManager + PropertyOwner membership on the new tenant (`IsPrimary =
  false` -- they already have a primary elsewhere; `SwitchContext`, not this endpoint, moves
  them into it), the same "operator is also deed owner" pairing
  `AuthController.ProvisionWorkspaceAsync` already grants on self-registration.
- **New mechanism: `ITenantStampOverride`** (`Ten21.Application.Abstractions`, scoped,
  implemented in `Ten21.Infrastructure.Persistence.TenantStampOverride`) -- an explicit,
  per-request, per-entity-instance override for which `TenantId`
  `Ten21DbContext.ApplyTenantStamping` stamps on insert, needed because this is the first
  authenticated endpoint that legitimately writes a row belonging to a DIFFERENT tenant than
  the caller's own currently-resolved `ITenantContext` (granting membership in the brand-new
  workspace, while the request itself is still scoped to whichever tenant the caller called
  from). `TenantContext.SetTenant` itself explicitly refuses to be called twice per request
  (guards against exactly the mid-request tenant-mutation `ARCHITECTURE.md`/US-04 warn
  against), so the fix couldn't be "just re-resolve the context" -- it needed its own
  narrow escape hatch. Deliberately entity-instance-scoped (reference equality), same
  pattern as `IHardDeleteOverride`: the `Ten21DbContext` constructor's third parameter is
  optional (defaults `null`), so every existing `new Ten21DbContext(options, tenantContext)`
  call site across the whole test suite kept compiling unchanged -- only the real
  DI-constructed app (and any test that explicitly wants it) gets the override wired in.
- **A real security question raised live mid-build, resolved by explanation rather than new
  code**: could a user who's PropertyManager in one workspace and merely a Tenant in another
  end up making PropertyManager-level changes in the Tenant workspace? Already structurally
  impossible: `TenantMiddleware` resolves `ITenantContext` from the signed JWT's own
  `tenant_id` claim ONLY (a client-supplied `X-Tenant-Id` header is deliberately never read
  -- see that class's own doc comment), and role + tenant_id are minted together into the
  SAME signed token by `SwitchContext`/`Login`. There is no code path where a
  PropertyManager-role token could ever pair with a different workspace's `tenant_id`. US-27
  proves this live as an explicit test case rather than leaving it as an assertion.

**Deliberately deferred:** an "invite another person into your tenant" flow (the
alternative multi-tenant source considered and set aside in favor of self-service portfolio
expansion) is still open for a future sprint if a genuine PMC-staffing scenario needs it.

### US-27: Switch-Context Test Coverage

**As a** Security Lead, **I want** the existing context-switch endpoint proven correct under
real conditions, **so that** a feature this security-sensitive isn't running on faith.

- **Primary Role:** Security Lead (test design/verification).
- **Authorized Secondary Roles:** N/A -- this story adds tests, not a capability.
- **Prohibited Roles:** N/A.
- **Required Permission Claims:** None (no code changed in `OrganizationController` itself
  this story -- see the Executive Summary for why no new permission claim was added either).

**What shipped:**
- `OrganizationControllerTests` (unit) -- `OrganizationController` had **zero** test coverage
  before this sprint despite being live and security-sensitive since Phase 0 (US-04). Covers
  `GetTenants` (lists every membership across tenants) and `SwitchContext`: issues a token
  correctly scoped to the target tenant, 403s with no membership in the target, and 403s when
  the target tenant is outside the caller's current `Organization` even though a (real, valid)
  membership row exists there -- proving the SECOND, independent check in `SwitchContext`
  (not just "membership exists") actually does something. That last test needed a dedicated
  `CreateController(currentOrganizationId: ...)` seeding path: `TenantContext.SetTenant` can
  only be called once per instance, so a controller that first calls `AddWorkspace` (which
  establishes the `Organization` in the database) can't have its own already-resolved context
  "catch up" mid-test -- the org has to be seeded upfront, the way a real second
  request/token issued after the fact would actually carry it.
- **The concrete, provable version of the security question raised live during US-26**: a
  caller who's PropertyManager on their current tenant and *only* a Tenant (renter) on a
  different one gets a token that says exactly `"Tenant"` the moment they switch into that
  workspace -- decoded and asserted directly off the issued JWT's own claims, not inferred.
- `SwitchContextEndToEndTests` (integration, real HTTP pipeline) -- what only a live round
  trip can prove: `AddWorkspace` + `SwitchContext` actually working together (a property
  created with the newly-switched token lands in the new workspace and is invisible under the
  original token -- tenant isolation held for a real write, not just that the endpoint
  responded), and the cross-PM resident scenario (US-24's `LinkExistingUserToTenantAsync`,
  built but never exercised live until now) switching successfully between two completely
  unrelated PMs' tenants with zero further backend changes, exactly as the Executive Summary
  predicted.
- **Real gotcha hit writing the integration test**: `AuthController.Register` issues a full
  session directly (`IssueTokensAsync`, bypassing the mandatory-2FA gate entirely) -- only
  `Login` gates a mandatory-2FA role behind `verify-2fa`. The cross-PM test originally spent
  6+ `/api/auth/*` calls setting up two PMs the same way `TwoFactorEndToEndTests` does
  (register + login + verify-2fa each), blowing through `AuthRateLimiterPolicy`'s 5-req/min
  budget before it even got to the actual assertions. Fixed by extracting the access token
  straight from `Register`'s own response for PM setup (1 call each, not 3) -- worth
  remembering for any future integration test that needs a *working* PM session and doesn't
  specifically need to exercise `Login`'s own 2FA path.
