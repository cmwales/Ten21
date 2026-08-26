# BUSINESS RULES SPECIFICATION

## 1. Role & Occupancy Domain Governance

- Deed Owner Privileges: Financial ledger statements, official HOA election ballots, and ARC alteration requests are strictly reserved for `Property Owner` accounts.
- Tenant Delegation: A `Property Owner` may delegate `Tenant` access for their unit, granting the occupant rights to submit work orders and view community notices while preserving owner-only ledger and voting confidentiality.
- Zero-Touch Automated Onboarding: Instant self-service registration provisions a new HOA tenant record, sets up baseline roles, and issues administrator credentials without manual staff intervention.

## 2. Property Management Company (PMC) Portfolio Rules

- Legal Entity Separation: Distinct corporate entities managed by a single PMC must maintain separate financial ledgers, bank accounts, and `TenantId` scopes. Commingling data or ledgers across child tenants is strictly prohibited.
- Staff Role Scoping: PMC executives can delegate staff members to specific child properties or across an entire portfolio without exposing corporate owner/board data between unrelated HOAs.

## 3. Accessibility, Localization & Usability Standards

- Mobile-First Single-Action UI: Every view must maintain a single primary call-to-action, a responsive card-stack layout for phones/tablets, and minimum 48×48px touch targets.
- Accessibility Mandate (WCAG 2.1 AA): All UI views must meet 4.5:1 color contrast ratios, support relative `rem` font scaling, include visible keyboard focus rings (`focus-visible:ring-2`), and utilize explicit `aria-label` tags for screen readers.
- North American Multi-Language Support (i18n): All system UI labels are fully localized via `@ngx-translate` supporting English (`en-US`), Canadian French (`fr-CA`), and US Spanish (`es-US`). Initial locale auto-detects from `navigator.language` while allowing manual overriding saved to user preferences.

## 4. Statutory Payment Waterfall Allocation (Sprint 7)

- Order of Application: A logged payment is applied to a unit's outstanding `Charges` in strict priority order — (1) Late Fees/Interest, (2) Legal Fees, (3) Base Rent, (4) Add-Ons, (5) Special Assessments — never chosen per-charge by the PM. Charges of equal priority are applied oldest-`DueDate`-first.
- Partial & Overpayment Handling: A payment that doesn't fully cover a charge partially satisfies it and moves on; a payment left over once every outstanding charge is satisfied is recorded on the `PaymentTransaction` (so it still counts toward the unit's balance as a credit) but is not tied to any one charge.
- Locked Charges: Once any amount has been allocated to a `Charge`, its `Amount` can no longer be edited, deleted, or voided directly — the only correction path is a `ChargeAdjustment` (a signed credit/debit with a mandatory reason), preserving an audit-compliant history instead of rewriting what was actually charged or paid.

## 5. Credit Store, Deposit Escrow & Reversals (Sprint 8)

- Manual Drawdown Only: Applying a resident's retained overpayment credit against outstanding charges ("Apply Credits to Charges") and settling a security deposit are both single PM-triggered actions with no automated background engine — this codebase has no recurring/anchor-date billing worker (Phase 2), so credit never silently drains onto a future charge the moment it's posted.
- Unit-Scoped Credit: A payment's leftover `UnallocatedAmount` can satisfy any outstanding charge on the same unit, regardless of which resident originally overpaid — matching the "billed to the unit" principle already established for `Charge` — but each `CreditAllocation` still records the source payment (and therefore the paying resident) for refund routing and history.
- Escrow Liability Separation: A `SecurityDeposit` is never a `Charge` and its settlement is never a `PaymentTransaction` — escrow money applied against a move-out balance (`DepositSettlementAllocation`) must never be indistinguishable from rental income in the ledger or on a statement.
- Full Settlement Only: Settling a deposit always resolves the entire `AmountHeld` in one atomic action — whatever isn't absorbed by outstanding charges is disbursed as a `RefundTransaction` (`Reason = DepositReturn`). There is no partial-settlement or re-open-after-settle flow.
- Audit-Compliant Reversal: Reversing a `PaymentTransaction` never soft-deletes or edits payment history — it un-links the payment's `PaymentAllocation`/`CreditAllocation` rows (restoring the affected charges to Unpaid, since balance is always computed live), zeroes its `UnallocatedAmount`, and requires a mandatory `ReversalReason`. Reallocating a payment across properties/residents is implemented as a reversal plus a brand-new payment, cross-referenced via `ReallocatedToId`, rather than mutating the original payment's property/resident in place.
