using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ten21.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Full-stack audit finding (2026-08-27): every ITenantScopedEntity table added since
    /// InitialCreate (Sprint 3 onward -- charges, payments, leases, residents, and now
    /// workspace_settings) went live with only the EF Core global query filter and NO
    /// Postgres RLS policy, silently violating CLAUDE.md's "isolation is enforced in two
    /// layers -- never skip either" rule for 15 tables. InitialCreate's own RLS block covered
    /// exactly the 3 tables that existed at the time (properties, document_attachments,
    /// audit_logs); nothing since then re-ran that step for new tables, and nothing (test or
    /// otherwise) caught the drift. This migration closes the gap for every currently missing
    /// table in one pass. tenant_memberships remains deliberately excluded -- see its own
    /// long-form comment in sql/rls-policies.sql for why (the auth bootstrap problem).
    /// Same "ALTER ... ENABLE/FORCE, then CREATE POLICY" pattern as InitialCreate, and same
    /// "TenantId" (not tenant_id) quoting -- see that migration's own comment on why the
    /// column name must stay case-preserved.
    /// </summary>
    public partial class AddRowLevelSecurityForLedgerLeaseAndResidentTables : Migration
    {
        private static readonly string[] Tables =
        [
            "charges",
            "charge_adjustments",
            "payment_transactions",
            "payment_allocations",
            "credit_allocations",
            "refund_transactions",
            "security_deposits",
            "deposit_settlement_allocations",
            "leases",
            "lease_recurring_charges",
            "unit_tiers",
            "unit_groups",
            "resident_profiles",
            "emergency_contacts",
            "workspace_settings",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"""
                    CREATE POLICY tenant_isolation_{table} ON {table}
                        USING ("TenantId" = current_setting('app.current_tenant_id', true)::uuid)
                        WITH CHECK ("TenantId" = current_setting('app.current_tenant_id', true)::uuid);
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation_{table} ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }
        }
    }
}
