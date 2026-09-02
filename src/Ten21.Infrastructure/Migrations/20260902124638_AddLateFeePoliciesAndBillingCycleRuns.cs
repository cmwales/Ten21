using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ten21.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLateFeePoliciesAndBillingCycleRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_cycle_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TriggeredBy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_cycle_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "late_fee_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    GracePeriodDays = table.Column<int>(type: "integer", nullable: false),
                    PolicyType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BaseAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    PercentageRate = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    DailyAccrualRate = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    MaxFeeCap = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_late_fee_policies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_cycle_runs_Status",
                table: "billing_cycle_runs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_billing_cycle_runs_TenantId_RunDate",
                table: "billing_cycle_runs",
                columns: new[] { "TenantId", "RunDate" });

            migrationBuilder.CreateIndex(
                name: "IX_late_fee_policies_LeaseId",
                table: "late_fee_policies",
                column: "LeaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_late_fee_policies_TenantId",
                table: "late_fee_policies",
                column: "TenantId");

            // late_fee_policies is ITenantScopedEntity -- same "ALTER ... ENABLE/FORCE, then
            // CREATE POLICY" pattern as every other tenant-scoped table (see
            // AddRowLevelSecurityForLedgerLeaseAndResidentTables). billing_cycle_runs is
            // deliberately NOT tenant-scoped (same precedent as the tenants table itself) --
            // no RLS policy for it here on purpose.
            migrationBuilder.Sql("ALTER TABLE late_fee_policies ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE late_fee_policies FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation_late_fee_policies ON late_fee_policies
                    USING ("TenantId" = current_setting('app.current_tenant_id', true)::uuid)
                    WITH CHECK ("TenantId" = current_setting('app.current_tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_late_fee_policies ON late_fee_policies;");
            migrationBuilder.Sql("ALTER TABLE late_fee_policies DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "billing_cycle_runs");

            migrationBuilder.DropTable(
                name: "late_fee_policies");
        }
    }
}
