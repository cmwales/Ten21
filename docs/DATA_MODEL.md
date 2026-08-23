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
