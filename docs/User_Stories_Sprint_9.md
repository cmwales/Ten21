# Ten21 - Sprint 9 Recurring Billing Engine & Automated Late Fees User Stories

Builds the recurring anchor-date billing engine deferred since Sprint 8 ("no
recurring/anchor-date engine exists in this codebase yet") -- unifies base rent and
add-on sub-charges into one generic recurring charge template, automates statutory late
fee assessment, and adds a read-time 30-day dues projection. Follows the same format as
`User_Stories_Sprint_4.md`. Per `FEATURES.md`'s Feature Specification Standards, every
story below declares its Primary Role, Authorized Secondary Roles, Prohibited Roles, and
Required Permission Claims.

## 1. Executive Summary & Core Design Directives

These decisions came out of a design discussion before any branch was cut, specifically
to avoid re-litigating them mid-implementation:

- **Base rent is unified into `LeaseRecurringCharge` templates (`Category = BaseRent`),
  not kept as a special case.** Today `Lease.MonthlyBaseRent`/`Lease.DueDayOfMonth` are
  separate fields from `LeaseRecurringCharge` (Sprint 6's add-on-only table). This sprint
  migrates every existing `Lease` into a generated `LeaseRecurringCharge(Category =
  BaseRent)` row and both fields' semantics move onto the template. Confirmed explicitly
  over the alternative (leave base rent on `Lease`, scope templates to `AddOn`/
  `SpecialAssessment` only) because it matches the sprint's "Unified Billing Engine"
  framing and avoids two parallel recurrence code paths.
- **`DueDayOfMonth` is stored honestly (1-31) and clamped at runtime, replacing the old
  1-28 cap.** `Lease.DueDayOfMonth`'s prior doc comment described a deliberate 1-28
  constraint specifically to dodge the short-month problem. This sprint reverses that
  choice project-wide: a template can store `DueDayOfMonth = 31`, and execution always
  computes `min(DueDayOfMonth, DaysInMonth(Year, Month))` -- so "the 31st" correctly
  resolves to Feb 28/29 without ever mutating the stored value.
- **`LateFeePolicy.MaxFeeCap` is a cumulative cap, not a per-instance one.** Once the sum
  of every `LateFee` charge posted against one overdue balance reaches `MaxFeeCap`, no
  further late fee posts until the balance is paid down -- this matters most for
  `DailyAccruing`/`Hybrid` policies, which would otherwise compound indefinitely.
- **Ten21.Api exposes an endpoint that does the work; it does not schedule anything
  itself.** No `BackgroundService`/`PeriodicTimer`/Hangfire/Quartz is built in this
  codebase for this sprint. Scheduling and orchestrating the nightly run across every
  tenant is entirely the responsibility of a separate future "owner site" (see §4) --
  Ten21.Api's job stops at "give me one endpoint that runs one tenant's cycle correctly,"
  called by whatever external caller decides it's time.
- **Execution goes through a real HTTP call, not a direct business-layer shortcut**, even
  though the caller is external rather than in-process. Every tenant-scoped query depends
  on `ITenantContext`, which today is only ever populated one way: `TenantMiddleware`
  reading a request. Routing the owner site's call through the same
  `POST api/billing/run-cycle` endpoint a human would hit means the run goes through
  `TenantMiddleware`, the EF query filter, RLS, and the audit interceptor exactly like a
  real request would -- no second, divergent tenant-context-setting code path to keep in
  sync.
- **CORRECTION (found while implementing US-45): `TenantMiddleware` never trusts a
  client-supplied `X-Tenant-Id` header.** Its own doc comment is explicit: tenant identity
  comes from signed JWT claims ONLY -- a deliberate US-01 decision, specifically so a
  client can never forge tenant access just by setting a header. The bullet below (an
  earlier draft) got this wrong. The real design ended up as **two routes**:
  - `POST api/billing/run-cycle` -- the caller's own ambient tenant, resolved by
    `TenantMiddleware` from a normal JWT exactly as documented below.
  - `POST api/billing/run-cycle/{tenantId:guid}` -- an EXPLICIT tenant in the route
    itself, for a caller with no JWT for that tenant (the internal scheduler, or a
    SuperAdmin retrying a specific tenant). `BillingCycleService.RunCycleForTenantAsync`
    calls `ITenantContext.SetTenant(tenantId)` directly rather than relying on
    `TenantMiddleware` -- legitimate because the caller already cleared a strong,
    separate authentication bar (the internal API key, or a real SuperAdmin JWT) before
    ever naming which tenant to touch, unlike a bare header any client could set.
  Both routes share one policy, `Permissions.Billing.RunCycle` -- no `propertyId` in
  either route; the service internally iterates every property/lease *within* that one
  tenant inside the single transaction. This deliberately breaks from the
  `api/properties/{propertyId}/...` nesting convention every other ledger controller
  uses, because the whole point of "one atomic cycle per site" is that it covers every
  property that tenant owns in one call, not one call per property. `run-cycle` also
  doubles as a manual trigger/backfill button a PM can click -- consistent with this
  codebase's existing precedent (credit drawdown, deposit settlement) of a PM-triggered
  action rather than silent automation.
- **`Permissions.Billing.RunCycle` is a new permission, deliberately distinct from
  `Permissions.Lease.Manage`**, satisfied by either a normal permission claim (granted to
  PropertyManager, and to SuperAdmin via `Permissions.All`) OR the internal API key
  (`InternalApiKeyAuthorizationHandler`, running alongside the existing
  `PermissionClaimAuthorizationHandler` for the same requirement type -- either handler
  succeeding satisfies the policy). Kept separate from `Lease.Manage` so a leaked internal
  key can only ever trigger a billing run, never anything else that permission would
  otherwise unlock (e.g. editing a lease directly). It's an unattended machine caller with
  no logged-in human behind each call, and normal login can't cleanly support that
  (2FA/lockout policy assume a person is present to respond) -- so it authenticates via a
  dedicated `X-Internal-Api-Key` header (`Internal:ApiKey` config, unset/fail-closed by
  default) instead, validated by a small, separate auth path. This is distinct from the
  admin list endpoint below, gated by a second new permission (`Permissions.Billing.
  ViewRuns`) that the internal key deliberately does NOT satisfy -- its purpose is
  narrowly "trigger a cycle," not "read every tenant's billing history."
- **A new `GET api/billing/admin/tenants` endpoint (same `Permissions.Billing.RunCycle`
  policy as the trigger routes, so the internal key satisfies it too) lets the owner site
  discover which tenants exist to loop over.** The owner site is a separate application
  with no direct DB access, so it can't just query the `Tenants` table itself the way
  in-process platform code always has before -- it needs this one small enumeration
  endpoint, added to US-45's scope.
- **Admin retry ended up as the SAME endpoint as the scheduler, not a separate one.** The
  original plan (§4 below, as first drafted) had a dedicated
  `POST api/admin/billing/runs/{tenantId}/retry`. Implementing US-45 surfaced that a
  SuperAdmin retrying one tenant and the internal scheduler triggering one tenant are the
  exact same operation with the exact same inputs -- both just need "run this specific
  tenant's cycle." So both go through `POST api/billing/run-cycle/{tenantId}`, and the
  controller infers `BillingCycleRun.TriggeredBy` (Scheduled vs. ManualRetry) from HOW the
  caller authenticated (internal API key header present, vs. a real JWT) rather than a
  client-supplied flag -- a caller can't claim to be "the scheduler" by asking, only by
  actually presenting the key.
- **Tenants are processed strictly sequentially, never concurrently**, and **each
  tenant's entire cycle (charge generation + late fee assessment) is one atomic unit.**
  The Business Service wraps both steps in a single `Database.BeginTransactionAsync`,
  committing only if both succeed -- any exception anywhere in either step means nothing
  for that tenant posts that night. One tenant's failure is logged and the scheduler moves
  to the next tenant; it never aborts the whole nightly batch.
- **A rolled-back cycle must be safe to retry.** Charge generation checks for an existing
  `Charge` tied to the same `LeaseRecurringCharge` + due date before creating a new one --
  a retried run (whether the next night's scheduled run or a manual retry) never
  double-bills a charge that already posted successfully before an earlier partial
  failure.
- **Bulk "retry every failed tenant" is explicitly out of scope for this sprint.**
  Confirmed explicitly: the founder will drive that case (dozens/hundreds of failures)
  through a separate operator/"owner site" tool built later, calling the per-tenant retry
  endpoint programmatically. This sprint only needs to expose that one endpoint plus a way
  to list which tenants failed and why -- see §4 below for the full runbook.
- **Failure visibility is a read-time query, not a notification pipeline.** No
  email/SMS/push alerting is built -- that's exactly the "complex background notification
  infrastructure" this sprint's own goal explicitly avoids. Instead, a `BillingCycleRun`
  log entity (platform-level, deliberately **not** `ITenantScopedEntity` -- same precedent
  as `Tenant` itself, queried only by SuperAdmin/platform concerns) records every run
  attempt, and a lightweight failed-run count surfaces on the existing admin Dashboard.
- **Branch-per-story, same cadence as every prior sprint.** US-44 -> US-45 -> US-46 ->
  US-47, each its own branch, merged into `main` with its own passing build/tests before
  the next branch is cut. US-44 first since every other story depends on the template
  engine it builds.

## 2. User Story Summary Matrix

| ID | Story Title | User Story Statement | Core Acceptance Criteria |
| --- | --- | --- | --- |
| **US-44** | Generic Recurring Charge Template Engine | As a Property Manager, I want to configure recurring base rent and auxiliary line-item fee templates with multi-frequency schedules, so that regular charges generate automatically on their due dates. | `LeaseRecurringCharge` extended with `Category`, `RecurrencePattern`/`RecurrenceInterval`, `DueDayOfMonth`/`TargetDayOfWeek`/`SecondaryDueDay`, `EndStrategy`, `EffectiveStartDate`/`EffectiveEndDate`, `ProrationStrategy`, `IsPaused`; base rent migrated onto it; runtime date-clamping; idempotent generation via `POST api/billing/run-cycle` (tenant-wide, not per-property). |
| **US-45** | Configurable Late Fee & Penalty Engine | As a Property Manager, I want to attach configurable late fee policies to leases, so overdue balances calculate penalties automatically after a grace period. | `LateFeePolicy` (`GracePeriodDays`, `PolicyType`, `BaseAmount`, `PercentageRate`, `DailyAccrualRate`, `MaxFeeCap` as a cumulative cap); posts `Category = LateFee` charges at the existing statutory `AllocationPriority`; runs inside the same per-tenant transaction as US-44's charge generation; `BillingCycleRun` log + `GET api/billing/admin/runs` + `GET api/billing/admin/tenants` for the owner site's own scheduling loop. |
| **US-46** | Courtesy Fee Waiver & Audit Adjustment Engine | As a Property Manager, I want to waive or adjust late fees with mandatory reason tracking, so that statement balances stay accurate without violating audit history. | Direct edit/void permitted only on a late fee with $0 applied payments; once any payment is allocated, only a `ChargeAdjustment` (min. 5-character `Reason`) may adjust it. |
| **US-47** | Read-Time 30-Day Dues Projection Engine | As a Resident or Property Manager, I want to view a forecast of upcoming charges for the next 30 days, so future financial obligations are transparent. | Projects active templates across `Today <= ExecutionDate <= Today + 30` at read time only; zero impact on ledger balances; no background writes; reflects template changes immediately. |

## 3. Detailed User Stories & Implementation Guidance

_Filled in per story as each branch lands -- see the Executive Summary above for the
cross-cutting decisions that apply to all four._

### US-44: Generic Recurring Charge Template Engine

**As a** Property Manager, **I want** to configure recurring base rent and auxiliary
line-item fee templates with multi-frequency schedules, **so that** regular charges
generate automatically on their due dates.

- **Primary Role:** Property Manager (`Permissions.Lease.Manage`).
- **Authorized Secondary Roles:** None.
- **Prohibited Roles:** Non-owner Tenant, Vendor.
- **Required Permission Claims:** `Permissions.Lease.Manage` (template CRUD),
  `Permissions.Billing.RunCycle` (the manual `run-cycle` trigger -- a new, deliberately
  separate permission; see US-45's own notes on why).

**What shipped:**
- `RecurrencePattern`/`EndStrategy`/`ProrationStrategy` enums; `LeaseRecurringCharge`
  extended with `Category`, `Description`, `RecurrenceInterval`, `DueDayOfMonth`/
  `TargetDayOfWeek`/`SecondaryDueDay`, `EffectiveStartDate`/`EffectiveEndDate`, `IsPaused`.
  `Lease.MonthlyBaseRent`/`DueDayOfMonth` removed entirely -- base rent is now just the
  template row with `Category = BaseRent`, migrated via a data-backfill EF migration
  verified against real seeded Postgres data (including the `DueDayOfMonth = 31` clamping
  edge case).
- `RecurrenceSchedule` (pure date-math, unit tested per pattern) and `BillingCycleService`
  (the transactional generation engine) in `Ten21.Business/Billing`.
- `Charge.SourceRecurringChargeId` + a unique `(SourceRecurringChargeId, DueDate)` DB
  index -- idempotency enforced at the database layer, not just application logic.
- `POST api/billing/run-cycle` (`BillingController`).
- **Real bug found only by live browser verification, not by the build or any test**: a
  shared `<ng-template>` used twice via `*ngTemplateOutlet` in the new `LeaseDrawer` form
  silently dropped every `@for`-driven `<option>` list positioned after the first nested
  `@if` inside it. `ng build` and every HTTP-mocked component test passed regardless,
  because neither actually renders the real template tree. Fixed by duplicating the field
  markup directly instead of sharing it through `ngTemplateOutlet` -- see the fix commit
  on this story's branch, and the `docs/PRODUCTION_READINESS.md` entry logging it as a
  general lesson for future form work in this codebase.
- Verified live end-to-end (real browser, real backend, real Postgres): registered an
  account, created a property/resident/lease with the full new template shape (Base Rent
  + a Weekly add-on with `TargetDayOfWeek`), saved successfully, then ran the billing
  cycle from the Ledger page and confirmed a correct `$0` result (nothing was due yet for
  a lease that had just started) with zero console errors.

### US-45: Configurable Late Fee & Penalty Engine

**As a** Property Manager, **I want** to attach configurable late fee policies to leases,
**so that** overdue balances calculate penalties automatically after a grace period.

- **Primary Role:** Property Manager (`Permissions.Lease.Manage`).
- **Authorized Secondary Roles:** None.
- **Prohibited Roles:** Non-owner Tenant, Vendor.
- **Required Permission Claims:** `Permissions.Lease.Manage` (policy CRUD, on
  `LeasesController`), `Permissions.Billing.RunCycle` (trigger an explicit tenant's
  cycle), `Permissions.Billing.ViewRuns` (SuperAdmin-only run history read).

**What shipped:**
- `LateFeePolicy` (zero-or-one per Lease, unique index on `LeaseId`), `LateFeePolicyType`
  enum, `LateFeeCalculator` (pure per-type fee math, unit tested), and late fee assessment
  folded into `BillingCycleService.RunCycleAsync`'s existing transaction -- a failed late
  fee run rolls back that cycle's freshly-generated recurring charges too, not just
  itself (verified by a dedicated test forcing a mid-cycle failure). Assessment operates
  per-Property (Charges are Property-scoped, not Lease-scoped, matching this codebase's
  existing "billed to the unit" convention) using whichever Lease's policy is found for
  that property -- a documented, narrow limitation for the rare multi-simultaneous-lease
  case. Idempotency: `Flat`/`Percentage`/`Hybrid` assess once per delinquency episode
  (keyed to the oldest currently-overdue charge's own due date, stable until that debt is
  paid off); `DailyAccruing` assesses fresh each day the balance stays overdue.
  `LateFeePolicy.MaxFeeCap` enforced as a running cumulative total across every LateFee
  charge on the property, capping (not blocking) a fee that would exceed it.
- `GET`/`PUT`/`DELETE api/properties/{propertyId}/leases/{id}/late-fee-policy`
  (`LeasesController`) -- `GET` returns 204 (not 404) when no policy is configured yet,
  since that's a normal state for a lease, not a missing resource.
- `BillingCycleRun` (platform-level, not `ITenantScopedEntity` -- same precedent as
  `Tenant` itself) logs every attempt, success or failure, in a save deliberately separate
  from the (possibly rolled-back) billing transaction so the log entry always survives.
- **Design correction made while implementing, not while planning**: the Executive
  Summary above documents in detail why the original "internal caller sends an
  `X-Tenant-Id` header" idea was wrong (`TenantMiddleware` never trusts one), and how the
  real two-route design (`run-cycle` vs. `run-cycle/{tenantId}`) replaces it. `Permissions.
  Billing.RunCycle`/`InternalApiKeyAuthorizationHandler` implement that corrected design.
- Admin retry consolidated into the same `run-cycle/{tenantId}` route rather than a
  separate endpoint (see the Executive Summary's "Admin retry ended up as the SAME
  endpoint" note) -- `GET api/billing/admin/runs` (filterable) and
  `GET api/billing/admin/tenants` round out the admin/owner-site surface. See §4 for the
  full operational runbook this story is responsible for. No scheduler is built in this
  codebase itself -- the future owner site (§5) is what actually calls these on a timer.
- **Frontend scope, decided during implementation:** a late-fee-policy inline editor was
  added to `LeaseDrawer` (matching the existing move-in-charge toggle pattern), backend-
  and-frontend tested. The Dashboard failed-run-count badge described in the original
  Executive Summary draft was **not built this sprint** -- there is no existing
  SuperAdmin-specific portal page to attach it to yet (building one was out of scope), and
  the founder's own stated plan is to handle this kind of admin visibility through the
  future owner/operator site instead. `GET api/billing/admin/runs` is reachable today via
  any authenticated HTTP call even with no dedicated screen for it.
- Not verified against a second live browser round-trip after US-44's -- the late-fee-
  policy UI uses the same plain `@if`/`@for` pattern (no `ngTemplateOutlet`) already
  proven live in US-44, and is covered by HTTP-mocked component tests, but wasn't
  separately clicked through in a running browser. Flagged explicitly per this codebase's
  own standard rather than assumed equivalent.

### US-46: Courtesy Fee Waiver & Audit Adjustment Engine

**As a** Property Manager, **I want** to waive or adjust late fees with mandatory reason
tracking, **so that** statement balances stay accurate without violating audit history.

- **Primary Role:** Property Manager (`Permissions.Ledger.Write`).
- **Authorized Secondary Roles:** None.
- **Prohibited Roles:** Non-owner Tenant, Vendor.
- **Required Permission Claims:** `Permissions.Ledger.Write`.

**What shipped:** almost entirely already true by construction, not new work -- the
Audit Refinement Sprint's `ChargeService.EnsureUnlockedAsync` already blocks
`UpdateAsync`/`VoidAsync`/`DeleteAsync` on any charge with `allocatedAmount > 0`
(category-agnostic, so it already covered late fees the moment `Category = LateFee`
charges existed), and `CreateAdjustmentAsync` already required a non-blank `Reason`. The
one genuine gap: no minimum length was enforced (a `Reason` of `"ok"` passed). Added a
`reason.Length < 5` check to `CreateAdjustmentAsync`'s existing validation, plus a unit
test proving the rejection.

### US-47: Read-Time 30-Day Dues Projection Engine

**As a** Resident or Property Manager, **I want** to view a forecast of upcoming charges
for the next 30 days, **so that** future financial obligations are transparent.

- **Primary Role:** Property Manager / Resident (`Permissions.Ledger.Read`).
- **Authorized Secondary Roles:** N/A -- both named roles read the same projection scoped
  to what they're each already authorized to see.
- **Prohibited Roles:** N/A.
- **Required Permission Claims:** `Permissions.Ledger.Read`.

**What shipped, with one scope correction made during implementation:** the "As a
Resident..." framing in this story's own statement runs directly into an existing,
explicit `CLAUDE.md` invariant -- `RolePermissions`' Tenant (non-owner resident) bundle
has never granted `Permissions.Ledger.Read` at all, and CLAUDE.md calls the non-owner
Tenant hard-block on financial ledgers "a security invariant, not a UX preference." A
30-day upcoming-dues forecast is ledger-adjacent financial information, so this shipped
**Property-Manager-only**, matching that existing invariant, rather than silently
widening resident access to satisfy the story's literal wording. Extending it to
residents is a deliberate, separate permission decision for later, not something folded
in here.
- `DuesProjectionService` (`Ten21.Business/Billing`) -- pure read, reuses
  `RecurrenceSchedule.IsDueOn` across `[Today, Today + 30]` per active template, zero
  writes (proven by a dedicated test asserting `Charges` stays empty after calling it).
- `GET api/properties/{propertyId}/charges/projection` (`ChargesController`).

## 4. Operational Runbook: Handling a Failed Nightly Run

Captured explicitly per the founder's request, since this is the one piece of this sprint
a human has to act on after the fact rather than something the system fully resolves
itself.

**What happens when a tenant's nightly cycle fails:** the owner site's scheduler calls
`GET api/billing/admin/tenants` to get the full tenant list, then calls `POST
api/billing/run-cycle/{tenantId}` once per tenant, sequentially, using the internal API
key. Because the whole cycle (charge generation + late fee assessment) runs inside one
transaction on Ten21's side, a failure anywhere rolls back everything for that tenant for
that night -- no partial charges are left posted. A `BillingCycleRun` row is written
either way (`Status = Success` or `Failed`, with `ErrorMessage` populated on failure,
`TriggeredBy = Scheduled`), and the owner site's scheduler logs the failing tenant id and
moves on to the next tenant. One tenant's failure never blocks or delays any other
tenant's run that night.

**How the site admin finds out:** there is no email/push alert (deliberately -- see the
Executive Summary's notification-avoidance rationale), and no Dashboard badge either --
that piece was scoped out of this sprint (see the Frontend Scope note in US-45's "What
shipped" below). What shipped instead: `GET api/billing/admin/runs`
(`Permissions.Billing.ViewRuns`, SuperAdmin-only, filterable by `Status`, `TenantId`,
date range) lists every run with its `ErrorMessage`, so the admin can see *why* a given
tenant failed, not just that it did -- reachable today via a normal authenticated HTTP
call (curl, Postman, a script) even with no dedicated screen for it yet.

**How to fix it, at either scale:** both scales end up calling the exact same endpoint --
`POST api/billing/run-cycle/{tenantId}` -- with a SuperAdmin's own JWT (no internal API
key needed; that policy is satisfied by their normal `Permissions.All` claim bundle too).
The controller records the resulting `BillingCycleRun` row as `TriggeredBy = ManualRetry`
since it detects a real JWT was used, not the internal key.
- **A handful of failures (1-2 tenants):** the admin reads the error via `GET
  api/billing/admin/runs`, fixes whatever caused it on that tenant's data, then calls
  `POST api/billing/run-cycle/{tenantId}` for that one tenant. Because charge generation
  is idempotent (see the Executive Summary), retrying after a partial failure is always
  safe -- nothing that already posted successfully gets duplicated.
- **Many failures at once (dozens/hundreds -- e.g. a systemic bug affecting most
  tenants):** clicking a retry button per tenant doesn't scale, and no bulk "retry all
  failed" endpoint/UI was built this sprint (explicitly deferred, per the founder's own
  direction). `GET api/billing/admin/runs?status=Failed` and
  `POST api/billing/run-cycle/{tenantId}` are both already exactly what's needed for an
  external tool to drive this case: list every failed tenant id, then call the per-tenant
  trigger endpoint for each one in a loop. The founder plans to build this as (or as part
  of) a separate operator/"owner site" -- a dedicated internal tool for running and fixing
  platform-level operational issues like this one, out of scope for Ten21's own tenant-
  facing product.

## 5. Follow-On: The Owner/Operator Site

Raised while scoping this sprint, not yet its own sprint: a dedicated internal tool
(separate from Ten21's tenant-facing product) for the founder to run and fix
platform-level operational issues -- starting with scheduling the nightly
`POST api/billing/run-cycle/{tenantId}` call across every tenant returned by
`GET api/billing/admin/tenants`, and driving bulk retries the same way when many tenants
fail at once (see §4). This sprint's API surface is deliberately built to be that tool's
foundation -- everything it needs to do its job already exists as a plain, internal-API-
key-gated endpoint by the time this sprint ships. Tentatively proposed as the sprint
immediately after this one; not yet scoped into its own user stories.
