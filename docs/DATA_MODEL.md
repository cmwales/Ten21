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
