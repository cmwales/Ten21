# FEATURES SPECIFICATION

## 1. Feature Specification Standards

- Mandatory Security & Role Definition: Every user story added to `FEATURES` must explicitly define:
  1. Primary Role: As a [Role]...
  2. Authorized Secondary Roles: Which other roles can perform or view this action.
  3. Prohibited Roles: Which roles are explicitly barred (e.g., Tenants barred from financial ledgers).
  4. Required Permission Claims: e.g., `Permissions.WorkOrders.Write`, `Permissions.Ledger.Read`.
- User Story Format: "As a [Role], I want [Action], so that [Benefit]."
