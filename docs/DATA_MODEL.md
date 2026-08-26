# DATA MODEL SPECIFICATION

## 1. Financial & Payment Token Schema

### 1.1 PaymentMethods Table

- `Id` (Guid, PK): Unique payment method identifier.
- `TenantId` (Guid, FK, Indexed): Multi-tenant isolation identifier. Enforced via EF Core Global Query Filter.
- `ResidentId` (Guid, FK, Indexed): Reference to the Property Owner or Resident.
- `PaymentToken` (nvarchar(255), NotNull): Tokenized reference returned from payment processor (e.g., `pm_1N9x...`). Zero raw banking data stored.
- `BankName` (nvarchar(100), Nullable): Display metadata (e.g., "Chase").
- `AccountType` (nvarchar(50), NotNull): e.g., Checking, Savings.
- `LastFour` (nvarchar(4), NotNull): Masked display account reference.
- `IsDefault` (bool, NotNull): Flag indicating primary payment account for automated ACH dues.

## 2. Organization & Portfolio Schema

### 2.1 Organizations Table

- `Id` (Guid, PK): Unique parent organization identifier for PMCs.
- `Name` (nvarchar(200), NotNull): Corporate name of the Property Management Company.
- `SubscriptionTier` (nvarchar(50), NotNull): Portfolio subscription billing plan.
- `CreatedAt` (DateTimeOffset, NotNull): Registration timestamp.

### 2.2 Tenants Table Updates

- `OrganizationId` (Guid, FK, Nullable): Optional parent link associating a child HOA/property tenant to a parent PMC `Organization`. Self-managed HOAs leave this column `NULL`.

## 3. Charges & Payment Ledger Schema (Sprint 7)

No automated recurring-billing engine exists yet (Phase 2) — a PM manually posts every charge each period, rent included, categorized via `charges.Category`. Charges stay unit-scoped (billed to the door, not an occupant — tester feedback); payments and adjustments are the only rows with a human owner or reason attached. `PaymentStatus` (Unpaid/Partial/Paid) and a unit's running `Balance` are never stored — both are computed at read time from these tables (`Balance = ΣActiveCharges.Amount + ΣDebitAdjustments − ΣPaymentTransaction.AmountPaid − ΣCreditAdjustments`).

### 3.1 Charges Table

- `Id` (Guid, PK).
- `TenantId` (Guid, FK, Indexed): Multi-tenant isolation identifier.
- `PropertyId` (Guid, FK, Indexed): The unit this charge is billed to (never a resident).
- `Description` (nvarchar(200), NotNull), `Amount` (decimal(18,2), NotNull), `DueDate` (date, NotNull), `AccountingCode` (nvarchar(50), Nullable).
- `Category` (nvarchar(20), NotNull): `LateFee` | `Legal` | `BaseRent` | `AddOn` | `SpecialAssessment`.
- `AllocationPriority` (int, NotNull): Server-derived from `Category` (see `BUSINESS_RULES.md` §4's statutory waterfall order) — never client-supplied.
- `IsStatutoryLocked` (bool, NotNull, default `true`): Reserved for a future manual-priority-override UI; no such UI exists yet.
- `Status` (nvarchar(20), NotNull): `Active` | `Voided`. Distinct from the computed `PaymentStatus`.
- Soft-deletable (`IsDeleted`), audited. Locked (no direct `Amount` edit/delete/void) once any `PaymentAllocation` references it — corrections go through `ChargeAdjustments` instead.

### 3.2 PaymentTransactions Table

- `Id` (Guid, PK).
- `TenantId` (Guid, FK, Indexed), `PropertyId` (Guid, FK, Indexed): The unit the payment applies to (drives which unit's outstanding charges the waterfall runs against).
- `ResidentProfileId` (Guid, FK, Indexed, NotNull): The human payee. Required so an overpayment or a payment made before any charge exists still resolves to someone refundable, even after they transfer units or a co-tenant moves out.
- `PaymentDate` (date, NotNull), `AmountPaid` (decimal(18,2), NotNull), `TenderType` (nvarchar(20), NotNull: `Cash` | `Check` | `Zelle` | `Venmo` | `DirectDeposit`), `ReferenceNumber` (nvarchar(100), Nullable), `Notes` (nvarchar(500), Nullable).
- Append-only: no soft-delete, no update/delete endpoint. Correcting a mistake happens via a `ChargeAdjustment`, never by editing payment history.

### 3.3 PaymentAllocations Table

- `Id` (Guid, PK), `TenantId` (Guid, FK, Indexed).
- `PaymentTransactionId` (Guid, FK, Indexed), `ChargeId` (Guid, FK, Indexed, Restrict on delete): The many-to-many application of (a portion of) one payment to one charge. One payment can span several charges via the statutory waterfall; one charge can receive allocations from several payments over time (partial payments).
- `AllocatedAmount` (decimal(18,2), NotNull).

### 3.4 ChargeAdjustments Table

- `Id` (Guid, PK), `TenantId` (Guid, FK, Indexed).
- `TargetChargeId` (Guid, FK, Indexed).
- `AdjustmentType` (nvarchar(20), NotNull: `CreditAdjustment` | `DebitAdjustment`), `Amount` (decimal(18,2), NotNull), `Reason` (nvarchar(500), NotNull) — mandatory audit explanation.
- Append-only: no soft-delete, no update/delete endpoint. The only way to change what's owed on a charge that already has a payment allocated to it; also valid on an unlocked charge (e.g. a goodwill credit that shouldn't touch the charge's own posted `Amount`).

## 4. Credit Store, Deposit Escrow & Reversal Schema (Sprint 8)

Extends the Sprint 7 ledger with retained-overpayment credit, security deposit escrow, and audit-compliant payment reversal. Balance formula updated to: `Balance = ΣActiveCharges.Amount + ΣDebitAdjustments − ΣPaymentTransaction.AmountPaid − ΣCreditAdjustments − ΣCreditAllocations + ΣOverpaymentRefunds − ΣDepositSettlementAllocations` (only `RefundTransaction` rows with `Reason = OverpaymentRefund` are added back — a `DepositReturn` refund was never counted in `SumPayments` to begin with, so adding it back too would double-count it; a unit test caught this).

### 4.1 PaymentTransactions Table Updates

- `UnallocatedAmount` (decimal(18,2), NotNull, default `0`): The retained-credit remainder set once at `LogPayment` time (`AmountPaid − ΣwaterfallAllocations`), then drawn down over time by `CreditAllocation` rows or paid out via a `RefundTransaction`.
- `Status` (nvarchar(20), NotNull, default `Cleared`): `Cleared` | `Reversed`. A reversed payment is excluded from `SumPayments`/`AvailableCredit`.
- `ReversalReason` (nvarchar(250), Nullable): Mandatory audit explanation, populated only when `Status = Reversed`.
- `ReallocatedToId` (Guid, FK, Nullable, self-referencing, Restrict on delete): Cross-references the replacement `PaymentTransaction` created when this payment was reallocated to a different property/resident.

### 4.2 CreditAllocations Table

- `Id` (Guid, PK), `TenantId` (Guid, FK, Indexed).
- `SourcePaymentTransactionId` (Guid, FK, Indexed): The overpaid `PaymentTransaction` supplying the credit.
- `TargetChargeId` (Guid, FK, Indexed): The charge the credit was applied to. Counts toward `Charge.AllocatedAmount`/`PaymentStatus` identically to a `PaymentAllocation`.
- `AppliedAmount` (decimal(18,2), NotNull), `AppliedDate` (date, NotNull).
- Created only via the manual "Apply Credits to Charges" action (unit-scoped, oldest-payment-first, statutory priority order) — never by a background process. Deleted (not soft-deleted) if its source payment is later reversed.

### 4.3 RefundTransactions Table

- `Id` (Guid, PK), `TenantId` (Guid, FK, Indexed).
- `ResidentProfileId` (Guid, FK, Indexed), `PropertyId` (Guid, FK, Indexed).
- `Amount` (decimal(18,2), NotNull), `RefundDate` (date, NotNull).
- `TenderType` (nvarchar(20), NotNull: `Check` | `DirectDeposit` | `Zelle` — a narrower set than `PaymentTransaction.TenderType`, since cash/Venmo aren't realistic disbursement methods).
- `ReferenceNumber` (nvarchar(100), Nullable).
- `Reason` (nvarchar(20), NotNull: `DepositReturn` | `OverpaymentRefund`).
- Append-only, no link back to the source payment(s) it drew down — funds are drawn FIFO oldest-payment-first at refund time.

### 4.4 SecurityDeposits Table

- `Id` (Guid, PK), `TenantId` (Guid, FK, Indexed).
- `PropertyId` (Guid, FK, Indexed): The unit the deposit was collected for.
- `ResidentProfileId` (Guid, FK, Indexed): Defaults to the Primary Resident on the unit's active `Lease` when not explicitly specified (Dual-Anchor Attribution).
- `OriginalAmount` (decimal(18,2), NotNull), `AmountHeld` (decimal(18,2), NotNull): Mutable, starts equal to `OriginalAmount`, reaches `0` once settled.
- `CollectedDate` (date, NotNull).
- `Status` (nvarchar(20), NotNull): `Held` | `Settled`.
- Deliberately not a `Charge` and never touches `PaymentTransaction` — escrow liability, not rental income.

### 4.5 DepositSettlementAllocations Table

- `Id` (Guid, PK), `TenantId` (Guid, FK, Indexed).
- `SecurityDepositId` (Guid, FK, Indexed), `TargetChargeId` (Guid, FK, Indexed): The deposit-money-equivalent of `CreditAllocation` — counts toward `Charge.AllocatedAmount` identically but is excluded from `SumPayments`.
- `AppliedAmount` (decimal(18,2), NotNull), `AppliedDate` (date, NotNull).
- Created only by `SettleDeposit`, which applies the entire `AmountHeld` against outstanding charges in statutory priority order in one atomic action; any remainder becomes a `RefundTransaction` (`Reason = DepositReturn`).
