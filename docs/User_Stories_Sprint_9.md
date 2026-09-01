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
- **The endpoint is tenant-wide, not nested under a single property.** `POST
  api/billing/run-cycle` (`Permissions.Lease.Manage`) -- no `propertyId` in the route.
  Tenant identity comes from `X-Tenant-Id`/JWT via `TenantMiddleware`, same as every other
  tenant-scoped endpoint; the service internally iterates every property/lease *within*
  that one tenant inside the single transaction. This deliberately breaks from the
  `api/properties/{propertyId}/...` nesting convention every other ledger controller
  uses, because the whole point of "one atomic cycle per site" is that it covers every
  property that tenant owns in one call, not one call per property. It also doubles as a
  manual trigger/backfill button a PM can click -- consistent with this codebase's
  existing precedent (credit drawdown, deposit settlement) of a PM-triggered action
  rather than silent automation.
- **The owner site authenticates via a narrow internal API key, not a service-account
  JWT.** It's an unattended machine caller running on its own schedule with no logged-in
  human behind each call, and normal login can't cleanly support that (2FA/lockout policy
  assume a person is present to respond). So it authenticates via a dedicated
  shared-secret header validated by a small, separate auth path -- smallest blast radius
  if leaked (it can only trigger a billing run or list tenants, nothing else), no
  per-tenant service-account provisioning needed. This is distinct from the admin
  list/retry endpoints below, which a human SuperAdmin drives and which use normal JWT
  login.
- **A new `GET api/admin/tenants` endpoint (internal-API-key gated, returning just `Id`/
  `Name`) lets the owner site discover which tenants exist to loop over.** The owner site
  is a separate application with no direct DB access, so it can't just query the
  `Tenants` table itself the way in-process platform code always has before -- it needs
  this one small enumeration endpoint, added to US-45's scope.
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
| **US-45** | Configurable Late Fee & Penalty Engine | As a Property Manager, I want to attach configurable late fee policies to leases, so overdue balances calculate penalties automatically after a grace period. | `LateFeePolicy` (`GracePeriodDays`, `PolicyType`, `BaseAmount`, `PercentageRate`, `DailyAccrualRate`, `MaxFeeCap` as a cumulative cap); posts `Category = LateFee` charges at the existing statutory `AllocationPriority`; runs inside the same per-tenant transaction as US-44's charge generation; `BillingCycleRun` log + admin list/retry endpoints + `GET api/admin/tenants` for the owner site's own scheduling loop. |
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
- **Required Permission Claims:** `Permissions.Lease.Manage` (template CRUD + manual
  `run-cycle` trigger).

_What shipped: TBD once this branch lands._

### US-45: Configurable Late Fee & Penalty Engine

**As a** Property Manager, **I want** to attach configurable late fee policies to leases,
**so that** overdue balances calculate penalties automatically after a grace period.

- **Primary Role:** Property Manager (`Permissions.Lease.Manage`).
- **Authorized Secondary Roles:** None.
- **Prohibited Roles:** Non-owner Tenant, Vendor.
- **Required Permission Claims:** `Permissions.Lease.Manage`.

_What shipped: TBD once this branch lands. Includes the internal-API-key auth path,
`BillingCycleRun`, the admin list/retry endpoints, and `GET api/admin/tenants` -- see §4
below for the operational runbook this story is responsible for. No scheduler is built
here; the owner site (§4, likely a future sprint of its own) is what actually calls this
on a timer._

### US-46: Courtesy Fee Waiver & Audit Adjustment Engine

**As a** Property Manager, **I want** to waive or adjust late fees with mandatory reason
tracking, **so that** statement balances stay accurate without violating audit history.

- **Primary Role:** Property Manager (`Permissions.Ledger.Write`).
- **Authorized Secondary Roles:** None.
- **Prohibited Roles:** Non-owner Tenant, Vendor.
- **Required Permission Claims:** `Permissions.Ledger.Write`.

_What shipped: TBD once this branch lands._

### US-47: Read-Time 30-Day Dues Projection Engine

**As a** Resident or Property Manager, **I want** to view a forecast of upcoming charges
for the next 30 days, **so that** future financial obligations are transparent.

- **Primary Role:** Property Manager / Resident (`Permissions.Ledger.Read`).
- **Authorized Secondary Roles:** N/A -- both named roles read the same projection scoped
  to what they're each already authorized to see.
- **Prohibited Roles:** N/A.
- **Required Permission Claims:** `Permissions.Ledger.Read`.

_What shipped: TBD once this branch lands._

## 4. Operational Runbook: Handling a Failed Nightly Run

Captured explicitly per the founder's request, since this is the one piece of this sprint
a human has to act on after the fact rather than something the system fully resolves
itself.

**What happens when a tenant's nightly cycle fails:** the owner site's scheduler calls
`GET api/admin/tenants` to get the full tenant list, then calls `POST
api/billing/run-cycle` once per tenant, sequentially, using the internal API key. Because
the whole cycle (charge generation + late fee assessment) runs inside one transaction on
Ten21's side, a failure anywhere rolls back everything for that tenant for that night --
no partial charges are left posted. A `BillingCycleRun` row is written either way
(`Status = Success` or `Failed`, with `ErrorMessage` populated on failure), and the
owner site's scheduler logs the failing tenant id and moves on to the next tenant. One
tenant's failure never blocks or delays any other tenant's run that night.

**How the site admin finds out:** there is no email/push alert (deliberately -- see the
Executive Summary's notification-avoidance rationale). Instead:
- The admin Dashboard shows a failed-run count for last night's batch, computed at read
  time from `BillingCycleRun` -- a passive "something needs attention" signal that costs
  nothing to build or run.
- `GET api/admin/billing/runs` (SuperAdmin-only, filterable by `Status`, `TenantId`, date
  range) lists every run with its `ErrorMessage`, so the admin can see *why* a given
  tenant failed, not just that it did.

**How to fix it, at either scale:**
- **A handful of failures (1-2 tenants):** the admin reads the error via `GET
  api/admin/billing/runs`, fixes whatever caused it on that tenant's data, then calls
  `POST api/admin/billing/runs/{tenantId}/retry` for that one tenant. This re-runs the
  exact same transactional cycle on demand and writes a new `BillingCycleRun` row
  (`TriggeredBy = ManualRetry`). Because charge generation is idempotent (see the
  Executive Summary), retrying after a partial failure is always safe -- nothing that
  already posted successfully gets duplicated.
- **Many failures at once (dozens/hundreds -- e.g. a systemic bug affecting most
  tenants):** clicking a retry button per tenant doesn't scale, and building a bulk
  "retry all failed" endpoint/UI is explicitly deferred rather than built into this
  sprint. `GET api/admin/billing/runs?status=Failed` and `POST
  api/admin/billing/runs/{tenantId}/retry` are both already exactly what's needed for an
  external tool to drive this case: list every failed tenant id, then call the per-tenant
  retry endpoint for each one in a loop. The founder plans to build this as (or as part
  of) a separate operator/"owner site" -- a dedicated internal tool for running and fixing
  platform-level operational issues like this one, out of scope for Ten21's own tenant-
  facing product. Both admin endpoints authenticate via the admin's own normal JWT login
  (no new auth scheme needed for this path, unlike the scheduler's internal API key --
  a human is driving it, however they choose to call the API).

## 5. Follow-On: The Owner/Operator Site

Raised while scoping this sprint, not yet its own sprint: a dedicated internal tool
(separate from Ten21's tenant-facing product) for the founder to run and fix
platform-level operational issues -- starting with scheduling the nightly `POST
api/billing/run-cycle` call across every tenant returned by `GET api/admin/tenants`, and
driving bulk retries via `POST api/admin/billing/runs/{tenantId}/retry` when many tenants
fail at once (see §4). This sprint's API surface is deliberately built to be that tool's
foundation -- everything it needs to do its job already exists as a plain, internal-API-
key-gated endpoint by the time this sprint ships. Tentatively proposed as the sprint
immediately after this one; not yet scoped into its own user stories.
