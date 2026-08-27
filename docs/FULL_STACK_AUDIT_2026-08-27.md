# Ten21 Full-Stack Audit — 2026-08-27

Conducted as a Lead Software Architect / CISO / UI-UX Director pass across the entire repository: backend (.NET 9 / EF Core / PostgreSQL), frontend (Angular Standalone Components + Signals / Tailwind), and the visual design system. Three parallel research passes (backend architecture & security, frontend hygiene & state management, UI/UX vs. `DESIGN_SYSTEM.md`) fed this report; a set of the highest-severity, lowest-risk findings were fixed directly as part of this pass and are marked **Fixed** below. Everything else is **Recommended** for a deliberate follow-up (most require either a broader refactor, a team design decision, or dedicated test coverage beyond this pass's scope).

All fixes were verified: `dotnet build` (0 warnings/errors), `dotnet test` on `Ten21.UnitTests` (278/278 passing) and the two `RlsIsolationTests` (passing against a real Postgres container via Testcontainers), `ng build`, and `ng test` (130/130 passing).

---

## 1. Executive Summary & Critical Refactoring Actions

### Fixed in this pass

| # | Fix | Why it mattered | Files |
|---|---|---|---|
| 1 | **Backfilled Postgres Row-Level Security for 15 tenant-scoped tables** that had gone live with only the EF Core query filter as protection — a genuine violation of CLAUDE.md's "isolation is enforced in two layers, never skip either." Covers every ledger table (charges, payment_transactions, payment_allocations, charge_adjustments, credit_allocations, refund_transactions, security_deposits, deposit_settlement_allocations), leases/lease_recurring_charges, unit_tiers/unit_groups, resident_profiles/emergency_contacts, and the brand-new workspace_settings. | **Critical.** These are the financial-ledger and PII tables; a bug in the EF Core filter (a missed `IgnoreQueryFilters()`, a raw-SQL escape hatch, a future EF release-note regression) would have had zero backstop. | New migration `20260827225513_AddRowLevelSecurityForLedgerLeaseAndResidentTables.cs`; documented in `sql/rls-policies.sql` |
| 2 | **Added a regression test** proving the backfilled RLS actually blocks cross-tenant reads on a real Postgres server (not just SQLite/EF-filter tests), mirroring the existing `properties` test. | Nothing previously caught the 5-sprint drift that produced Fix #1 — this closes that hole for the `charges` table as a representative case. | `tests/Ten21.IntegrationTests/RlsIsolationTests.cs` |
| 3 | **Darkened the Financial Emerald design token** from `#059669` (~3.8:1 contrast on white/Neutral Surface, failing WCAG AA's 4.5:1) to `#047857` (~5.5:1 on white, still ~4.7:1+ on Slate Navy). User-approved change. | **Critical accessibility gap** — this single token affected primary CTAs, links, and status colors on essentially every screen in the app. | `frontend/src/styles.scss`, `docs/DESIGN_SYSTEM.md` |
| 4 | **Fixed three amber-on-light-background contrast failures** (~3.2:1) by switching to the solid `bg-amber` + `text-slate-navy` pattern already used correctly elsewhere in the app (deposit/charge status badges) instead of the failing `bg-amber/10 text-amber` pattern. | Medium accessibility — "Vacant" property badge, "Expiring Soon" lease badge, "Notice Given" resident indicator all failed AA contrast. | `property-list.ts`, `lease-drawer.html`, `resident-drawer.html` |
| 5 | **Added `min-width: 48px` to the global `.tap-target` utility** (it only had `min-height`), and added missing `focus-visible:ring-2` to the property-matrix select-all/select-row checkboxes and the payment-action-modal reverse/reallocate radio buttons — the only interactive controls in the app missing a visible focus ring. | Medium accessibility — WCAG 2.5.5 target size and 2.4.7 focus visibility. | `styles.scss`, `property-matrix.html`, `payment-action-modal.html` |
| 6 | **Fixed a card-styling inconsistency** on the Dashboard (`rounded-lg p-4 shadow-sm`) to match the `rounded-xl bg-surface-card p-6 shadow-xl` convention used on every other screen. | Low — pure visual consistency. | `dashboard.html` |

### Recommended, not applied in this pass

These are real findings but each needs either a broader multi-file refactor, a team decision, or dedicated test time that didn't fit safely into this same pass without risking an unreviewed, hard-to-verify diff:

1. **N+1 query in `ChargesController.GetCharges`** — 4 queries per charge, unbounded by pagination (High severity, perf). Should batch-load allocations/adjustments once and group in memory, the same pattern `BuildStatementAsync` already uses correctly a few lines below it in the same file.
2. **`AsNoTracking()` is used zero times anywhere in the codebase** — every read-only `GET` endpoint tracks entities it never mutates (Medium, perf, systemic).
3. **Duplicated helper logic across controllers**: `GetResidentNameAsync`-shaped lookups (3+ independent copies), `EnsurePropertyExistsAsync` (5 independent copies), and the "outstanding amount" waterfall calculation (3 independent copies across `PaymentsController`, `DepositsController`, `CreditsController` — the riskiest duplication, since a future business-rule change to one copy and not the others would silently corrupt ledger math). Recommend a shared service/extension method for each.
4. **9 Angular modal/drawer components share near-identical open/close/submit boilerplate** (`@Input open`, `OnChanges`, `@Output closed/saved`, `close()`) — good candidate for a shared base class or directive, and a natural opportunity to migrate to signal-based `input()`/`output()` at the same time.
5. **Hardcoded `$` currency prefix** instead of Angular's locale-aware `currency` pipe, in ~7 templates (`unit-statement.html` alone has 11 occurrences) — will render incorrectly for `fr-CA` locale users specifically (High, i18n correctness).
6. **No resource-based (ownership-inspecting) authorization handler exists**, despite CLAUDE.md/SECURITY.md calling for one — today's BOLA/IDOR defense is 100% convention-based (every controller manually re-scopes by parent route ID, verified correct everywhere it was checked), with no structural guardrail stopping a future controller from skipping it (Medium).
7. **No automated check that every `ITenantScopedEntity` has a matching RLS policy** — the exact gap that let Fix #1's problem happen is not yet prevented from recurring. Recommend a reflection-driven test mirroring `Ten21DbContext.OnModelCreating`'s own reflection loop, asserting a `pg_policies` row exists per tenant-scoped table.
8. **App header has no mobile nav collapse** — workspace switcher + up to 5 nav links + logout sit in one non-wrapping row; will crowd/overflow on narrow viewports (Medium, responsive).
9. Missing index on `ResidentProfile.UserId` (queried on every directory request) and a missing composite index backing the property-duplicate-detection query (Medium/Low, perf).
10. `DocumentsController.PresignUpload` never validates that `request.EntityId` belongs to the caller within their own tenant before accepting it (Low-Medium, BOLA-adjacent — doesn't cross tenant boundaries since the S3 key is tenant-prefixed server-side, but allows attaching an upload to an arbitrary in-tenant entity ID).
11. `Permissions.Workspace.SettingsRead/SettingsWrite` (added this sprint) isn't listed in `TenantRestrictedPermissionPrefixes`, unlike every other permission category's deliberate inclusion/exclusion, which is otherwise explicitly commented (Low, latent — Tenant isn't granted it today, so not exploitable, but the codebase's own defense-in-depth convention wasn't followed for this new category).
12. One `StatusCode(201, ...)` call (`ChargesController.CreateChargeAdjustment`) instead of the `CreatedAtAction` used by every other create endpoint — cosmetic API convention nit, not a taxonomy violation (Low).

---

## 2. Architectural & Security Findings Table

| Issue | Impact | File/Location | Status | Recommended Fix |
|---|---|---|---|---|
| 15 of 19 tenant-scoped tables had no Postgres RLS policy, only the EF Core filter | **Critical** | `sql/rls-policies.sql`; all migrations after `InitialCreate` | **Fixed** | New migration applies `ENABLE`/`FORCE ROW LEVEL SECURITY` + `CREATE POLICY` to all 15 tables |
| RLS backfill has no regression test proving it works against real Postgres, beyond a representative case | High | `tests/Ten21.IntegrationTests/RlsIsolationTests.cs` | **Partially fixed** (added `charges` case) | Add a reflection-driven test asserting every `ITenantScopedEntity` table has a `pg_policies` row |
| Financial Emerald token fails WCAG AA contrast (~3.8:1) on light surfaces | Critical (a11y) | `frontend/src/styles.scss`, `docs/DESIGN_SYSTEM.md` | **Fixed** | Darkened to `#047857` (~5.5:1) |
| `ChargesController.GetCharges` issues 4 queries per charge, unbounded by pagination | High (perf) | `src/Ten21.Api/Controllers/ChargesController.cs:39-57` | Recommended | Batch-load allocations/adjustments once, group in memory (mirror `BuildStatementAsync`) |
| Hardcoded `$` prefix instead of `currency` pipe — breaks `fr-CA` formatting | High (i18n) | `unit-statement.html` (×11), `ledger.html`, `charge-modal.html`, `lease-drawer.html`, `payment-details-modal.html`, `property-list.html`, `property-matrix.html` | Recommended | Replace with Angular's `| currency` pipe |
| No resource-based/ownership authorization handler exists despite SECURITY.md calling for one | Medium | `src/Ten21.Infrastructure/Authorization/*` | Recommended | Structural guardrail beyond the (currently correct, but convention-only) per-controller scoping |
| Duplicated "outstanding amount" waterfall calculation across 3 controllers | Medium | `PaymentsController`, `DepositsController`, `CreditsController` | Recommended | Extract to one shared service method |
| Duplicated resident-name and property-existence helpers across 5+ controllers | Medium | `PaymentsController`, `DepositsController`, `RefundsController`, `ChargesController`, `CreditsController`, `ResidentsController` | Recommended | Shared extension/service method |
| `AsNoTracking()` used zero times — every read-only GET tracks entities unnecessarily | Medium (perf) | Codebase-wide | Recommended | Add to read-only query paths |
| Missing index on `ResidentProfile.UserId`, queried on every directory request | Medium (perf) | `ResidentProfileConfiguration.cs` | Recommended | Add index |
| Property duplicate-detection query has no supporting composite index | Low-Medium (perf) | `PropertiesController.cs:340-358,590-638` | Recommended | Composite index on the 6-column tuple |
| `DocumentsController.PresignUpload` doesn't validate `EntityId` ownership within tenant | Low-Medium | `DocumentsController.cs:41-61` | Recommended | Add an ownership check before signing |
| `Permissions.Workspace.*` not listed in `TenantRestrictedPermissionPrefixes` (inconsistent with the file's own documented convention) | Low | `TenantRestrictedPermissionPrefixes.cs` | Recommended | Add explicit comment (safe today) or the prefix itself |
| 9 Angular modals/drawers duplicate open/close/submit boilerplate | Medium (frontend debt) | `pages/properties/*-modal/*.ts`, `*-drawer.ts` | Recommended | Shared base class/directive; opportunity to adopt `input()`/`output()` |
| Legacy `@Input()`/`@Output()` decorator API used throughout instead of signal-based `input()`/`output()` | Medium (frontend consistency) | Same 9+ components | Recommended | Migrate alongside the base-class extraction above |
| Amber-on-light-background contrast failures (3 spots) | Medium (a11y) | `property-list.ts`, `lease-drawer.html`, `resident-drawer.html` | **Fixed** | Switched to `bg-amber text-slate-navy` |
| `.tap-target` missing `min-width`; two controls missing `focus-visible:ring-2` | Medium (a11y) | `styles.scss`, `property-matrix.html`, `payment-action-modal.html` | **Fixed** | Added `min-width: 48px`; added ring classes |
| App header has no mobile nav collapse | Medium (responsive) | `shared/app-header/app-header.html` | Recommended | Hamburger/overflow menu below a breakpoint |
| Dashboard card styling deviated from the app-wide card convention | Low | `dashboard.html` | **Fixed** | Matched `rounded-xl bg-surface-card p-6 shadow-xl` |
| One `StatusCode(201,...)` instead of `CreatedAtAction` | Low | `ChargesController.cs:482` | Recommended | Switch to `CreatedAtAction` for a `Location` header |
| `AuthController` has no matching `AuthControllerTests.cs` | Informational | `tests/Ten21.UnitTests/` | No action — covered by `Ten21.IntegrationTests` instead (Identity doesn't unit-test well against SQLite) | — |

**Note on scope discipline**: token/RBAC/RFC-7807/BOLA conventions were otherwise found to be followed *consistently* — the audit did not find widespread violations of the codebase's own stated rules outside what's listed above. Component-level `.scss` files: **zero found** (fully compliant). Naming conventions: **fully consistent** across all 34+ Angular components. `console.log`/commented-out code/`TODO`/`FIXME`: **essentially none found**. RxJS subscription leaks: **none found** beyond one low-severity missed-`clearTimeout` case in `property-matrix.ts`'s autosave debounce.

---

## 3. Visual UI/UX Screen Audit Breakdown

Cross-cutting note: raw off-palette Tailwind colors (`bg-blue-500`, `text-red-600`, etc.) — **zero found** anywhere in the app; token discipline is a genuine strength. Status badges (Paid/Partial/Unpaid, Held/Settled, Active/Voided, Occupied/Vacant/Maintenance) all correctly pair color with a translated text label — none are color-only.

| Screen / Component | Design Token Issues | Layout/Responsive Issues | Accessibility Issues | i18n Issues |
|---|---|---|---|---|
| Login | none | Single clear CTA; consistent card | Emerald contrast — **fixed by token change** | none |
| Register | none | Good `sm:grid-cols-2` responsive; single CTA | Emerald contrast — **fixed** | Hardcoded phone placeholder `"(555) 123-4567"` |
| Complete Profile | none | Consistent card; single CTA | Emerald contrast — **fixed** | none |
| Activate | none | Consistent card; single CTA per state | Emerald contrast — **fixed** | none |
| Forgot Password | none | Consistent card; single CTA | Emerald contrast — **fixed** | none |
| Resend Activation | none | Consistent card; single CTA | Emerald contrast — **fixed** | none |
| Reset Password | none | Consistent card; single CTA | Emerald contrast — **fixed** | none |
| Verify Two-Factor | none | Consistent card; single CTA | Emerald contrast — **fixed** | none |
| Change Temp Password | none | Consistent card; single CTA | Emerald contrast — **fixed** | none |
| Dashboard | none | Card style **fixed** to match app convention | Emerald contrast — **fixed** | none |
| Ledger | none | Consistent card pattern; single implicit action | Emerald contrast — **fixed**; balance color conveyed via rose/emerald with the number itself still legible (not pure color-only) | none |
| Admin Settings | none | Consistent card; single CTA (Save) | Emerald contrast — **fixed**; checkbox wrapped in full-row label, effective tap target fine | none |
| Property List | none | Clear CTA hierarchy; table `overflow-x-auto` for mobile | Emerald contrast — **fixed**; Vacant badge contrast — **fixed** | none |
| Property Matrix | none | Minor `p-5` vs `p-6` card padding inconsistency (not fixed — cosmetic, low value) | Checkbox focus ring + tap-target width — **fixed**; emerald contrast — **fixed** | none |
| Property Import | none | Consistent card; table `overflow-x-auto` | Emerald contrast — **fixed** | none |
| Property Info Form | none | Responsive `sm:grid-cols-2/3` | Emerald contrast (checkbox accent) — **fixed** | none |
| Property Form Container | Rose reserved for alerts per spec, not misused | 4 equally-weighted header buttons + separate Cancel/Apply/Save — Apply vs. Save primary-action ambiguity (not fixed — needs a UX decision on which is primary) | Emerald contrast — **fixed** | none |
| Resident Drawer | none | Consistent drawer card | Close button target size (root-caused by `.tap-target` min-width — **fixed**); Notice-Given contrast — **fixed** | none |
| Lease Drawer | none | Consistent drawer card | Close button target size — **fixed** (same root cause); Expiring Soon badge contrast — **fixed** | none |
| Unit Statement | none | Up to 6 header actions, only one filled-primary but visually crowded (not fixed — needs a UX decision on grouping/overflow) | Emerald contrast — **fixed**; balance color-only concern same as Ledger | Hardcoded `$` — **recommended, not fixed** (11 occurrences) |
| PDF Viewer | none | Simple, single CTA | none (no emerald used here) | none |
| Log Payment Modal | none | Consistent modal card | Emerald contrast — **fixed** | none |
| Refund Credit Modal | none | Consistent modal card | Emerald contrast — **fixed** | none |
| Payment Action Modal | Rose used for a destructive primary action (reasonable, minor semantic drift from spec's "alerts only" framing — not fixed, low value) | Consistent modal card | Radio focus ring + tap-target — **fixed** | none |
| Collect Deposit Modal | none | Consistent modal card | Emerald contrast — **fixed** | none |
| Settle Deposit Modal | Rose as primary submit (same minor drift as above) | Consistent modal card | Rose contrast already passing | none |
| Charge Modal | none | Consistent 3-state modal; single Save per state | Emerald contrast — **fixed** | none |
| Payment Details Modal | none | Consistent modal card | Emerald contrast — **fixed** | Hardcoded `$` — **recommended, not fixed** |
| App Header | none | **No mobile nav collapse** — not fixed, needs a hamburger/overflow-menu design decision | Best-in-class focus rings/`aria` attributes already present | none |
| Language Selector | none | none | Fully compliant (`sr-only` label, `aria-label`, focus ring) | none |
| Toast Host | none | none | `role="status"` present; no color-only severity variants | none |
| Form Field (shared) | none | N/A (presentational wrapper) | Label correctly bound; error text already passes contrast | none |

### Deferred UX decisions (not mechanical fixes — need product/design input)

1. **Property Form Container**: 4 equal-weight header buttons plus a separate Cancel/Apply/Save row reads as multiple competing primaries. Recommend picking one clear primary action per screen state.
2. **Unit Statement**: up to 6 header actions crowd the top of the screen on smaller viewports. Recommend grouping secondary actions (Collect Deposit, Download PDF) behind an overflow/"More actions" menu, keeping Log Payment as the sole primary.
3. **Payment Action / Settle Deposit modals**: use Rose as a primary submit-button color for a "confirm this happened" action rather than strictly for alerts. This reads fine in practice (destructive/irreversible action → warm color), but is a deliberate deviation from `DESIGN_SYSTEM.md`'s literal scoping of Rose to "late fee warnings, delinquent account flags, urgent maintenance notices" — worth either updating the doc to acknowledge this use case or standardizing on a different treatment.
4. **Property Matrix** header card padding (`p-5`) vs. the rest of the app's `p-6` — cosmetic, low priority.
