-- ============================================================================
-- Ten21 - PostgreSQL Row-Level Security policies for US-01
-- ============================================================================
-- WHY THIS FILE EXISTS AS RAW SQL INSTEAD OF ALREADY BEING IN A MIGRATION:
-- This scaffold was generated in an environment without the .NET SDK / EF Core
-- tooling installed, so `dotnet ef migrations add InitialCreate` could not be
-- run here. Once you have the SDK locally:
--
--   1. dotnet ef migrations add InitialCreate --project src/Ten21.Infrastructure --startup-project src/Ten21.Api
--   2. Open the generated .../Migrations/<timestamp>_InitialCreate.cs
--   3. Inside Up(), after the CREATE TABLE calls, add:
--        migrationBuilder.Sql(@"
--            <paste the ALTER TABLE / CREATE POLICY statements below here>
--        ");
--   4. Inside Down(), add the mirror-image DROP POLICY statements (see bottom
--      of this file) so the migration is fully reversible.
--
-- Once that's done, Program.cs's dev-only `db.Database.MigrateAsync()` call
-- applies schema AND row-level security together, in one shot, exactly once
-- (EF's __EFMigrationsHistory table prevents it re-running on every restart) --
-- that's what "docker compose up && dotnet run" loading everything at runtime
-- actually relies on. Until you've done steps 1-4, MigrateAsync() only applies
-- table schema; RLS policies still need the manual run described in the
-- (now-removed) original version of this comment, or you can just run this
-- file directly against the dev database once with `psql` as a stopgap.
-- ============================================================================

-- The application's runtime DB role must NOT be a superuser/table-owner role,
-- or Postgres exempts it from RLS entirely (BYPASSRLS / table ownership both
-- bypass RLS by default). Confirm ten21_app is neither before relying on this.

ALTER TABLE properties ENABLE ROW LEVEL SECURITY;
ALTER TABLE properties FORCE ROW LEVEL SECURITY; -- applies even to the table owner

CREATE POLICY tenant_isolation_properties ON properties
    USING ("TenantId" = current_setting('app.current_tenant_id', true)::uuid)
    WITH CHECK ("TenantId" = current_setting('app.current_tenant_id', true)::uuid);

-- document_attachments (US-06) and audit_logs (US-07): both are only ever queried WITH an
-- already-resolved tenant context (authenticated endpoints, no login-style bootstrap
-- problem like tenant_memberships has), so both get the standard full RLS treatment.

ALTER TABLE document_attachments ENABLE ROW LEVEL SECURITY;
ALTER TABLE document_attachments FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_document_attachments ON document_attachments
    USING ("TenantId" = current_setting('app.current_tenant_id', true)::uuid)
    WITH CHECK ("TenantId" = current_setting('app.current_tenant_id', true)::uuid);

ALTER TABLE audit_logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_logs FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_audit_logs ON audit_logs
    USING ("TenantId" = current_setting('app.current_tenant_id', true)::uuid)
    WITH CHECK ("TenantId" = current_setting('app.current_tenant_id', true)::uuid);

-- Repeat this pattern (ENABLE + FORCE + CREATE POLICY) for every table backing an
-- ITenantScopedEntity as new entities are added in later phases. `tenants` and
-- `organizations` are deliberately excluded -- they are not themselves
-- tenant-scoped (see Domain/Entities/Tenant.cs and Organization.cs for why).
--
-- ============================================================================
-- POST-LAUNCH DRIFT, FOUND AND FIXED 2026-08-27 (full-stack audit):
-- ============================================================================
-- The "repeat this pattern" instruction above was NOT followed for five sprints.
-- Every ITenantScopedEntity table added after InitialCreate -- charges,
-- charge_adjustments, payment_transactions, payment_allocations,
-- credit_allocations, refund_transactions, security_deposits,
-- deposit_settlement_allocations, leases, lease_recurring_charges, unit_tiers,
-- unit_groups, resident_profiles, emergency_contacts, workspace_settings (15
-- tables) -- went live with the EF Core query filter as its ONLY isolation
-- layer, silently violating this file's own instruction and CLAUDE.md's "never
-- skip either" rule. Nothing (test, CI check, or review checklist) caught it,
-- because RlsIsolationTests.cs only ever asserted RLS on the original 3 tables.
--
-- Fixed by migration `AddRowLevelSecurityForLedgerLeaseAndResidentTables`
-- (src/Ten21.Infrastructure/Migrations/20260827225513_...), which applies the
-- exact same ENABLE/FORCE/CREATE POLICY statements to all 15 tables in one
-- pass, verified against a real Postgres container (RlsIsolationTests'
-- RawSql_CannotReadAnotherTenantsChargeRows_EvenBypassingEfCoreFilter).
--
-- This file's own listing above was NOT retroactively expanded to also list
-- those 15 tables' SQL verbatim -- the migration is now the single source of
-- truth for what RLS policies exist. Treat this file as historical rationale
-- (why RLS, why tenant_memberships is excluded, why FORCE/quoting matter), not
-- as a complete inventory going forward.
--
-- PROCESS FIX NEEDED so this can't silently recur a sixth time: add a test (in
-- either Ten21.UnitTests or Ten21.IntegrationTests) that reflects over every
-- ITenantScopedEntity CLR type -- the same reflection Ten21DbContext.
-- OnModelCreating already does for the EF Core filter -- and asserts a
-- matching `pg_policies` row exists for its table. No such test exists yet;
-- this comment is the tracking marker for that gap until one is written.
--
-- `tenant_memberships` is ALSO deliberately excluded, despite being ITenantScopedEntity,
-- and this one is worth understanding rather than assuming it's an oversight:
--
-- AuthController's login and refresh-token flows must look up a user's TenantMemberships
-- BEFORE any tenant context exists (there's no JWT yet -- that's the entire point of those
-- endpoints). They already bypass the EF Core filter for that one deliberate purpose via
-- IgnoreQueryFilters() -- see AuthController.ResolvePrimaryMembershipAsync and the
-- refresh-token handler.
--
-- IgnoreQueryFilters() only bypasses the EF Core filter, though -- it does nothing to a
-- Postgres RLS policy. If tenant_memberships had RLS enabled, TenantSessionInterceptor
-- would still be setting app.current_tenant_id to Guid.Empty for that request (no tenant
-- resolved yet), and the RLS policy would silently return zero rows regardless of
-- IgnoreQueryFilters() -- login would be structurally broken, not just filtered.
--
-- Net effect: tenant_memberships currently has ONE isolation layer (the EF Core filter),
-- not the usual two. If this needs hardening later, the standard pattern is a SEPARATE
-- Postgres role with BYPASSRLS, used only by the auth code path's connection -- not
-- disabling RLS globally. Flagging this as a deliberate, visible trade-off rather than
-- silently shipping a table that looks RLS-protected but isn't.

-- current_setting(..., true) -- the `true` means "missing_ok": if
-- TenantSessionInterceptor hasn't run yet (shouldn't happen in practice, but
-- fail closed rather than error), this returns NULL instead of throwing, and
-- `tenant_id = NULL` evaluates to false for every row -- i.e. zero rows,
-- matching the EF Core filter's fail-closed behavior exactly.

-- ============================================================================
-- Mirror image for the migration's Down() method:
-- ============================================================================
-- DROP POLICY IF EXISTS tenant_isolation_properties ON properties;
-- ALTER TABLE properties DISABLE ROW LEVEL SECURITY;
-- DROP POLICY IF EXISTS tenant_isolation_document_attachments ON document_attachments;
-- ALTER TABLE document_attachments DISABLE ROW LEVEL SECURITY;
-- DROP POLICY IF EXISTS tenant_isolation_audit_logs ON audit_logs;
-- ALTER TABLE audit_logs DISABLE ROW LEVEL SECURITY;
