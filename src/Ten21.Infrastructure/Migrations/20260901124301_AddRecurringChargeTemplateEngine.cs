using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ten21.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringChargeTemplateEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "lease_recurring_charges",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "lease_recurring_charges",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DueDayOfMonth",
                table: "lease_recurring_charges",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveEndDate",
                table: "lease_recurring_charges",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveStartDate",
                table: "lease_recurring_charges",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "EndStrategy",
                table: "lease_recurring_charges",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsPaused",
                table: "lease_recurring_charges",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProrationStrategy",
                table: "lease_recurring_charges",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceInterval",
                table: "lease_recurring_charges",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RecurrencePattern",
                table: "lease_recurring_charges",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SecondaryDueDay",
                table: "lease_recurring_charges",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetDayOfWeek",
                table: "lease_recurring_charges",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            // US-44 data backfill, while leases.MonthlyBaseRent/DueDayOfMonth still exist:
            // 1) every pre-existing lease_recurring_charges row (Sprint 6's add-on-only
            //    shape) gets real values for the new required fields instead of the
            //    placeholder empty-string/zero defaults AddColumn just applied above.
            migrationBuilder.Sql(@"
                UPDATE lease_recurring_charges lrc
                SET ""Category"" = 'AddOn',
                    ""RecurrencePattern"" = 'Monthly',
                    ""RecurrenceInterval"" = 1,
                    ""EndStrategy"" = 'LeaseAligned',
                    ""ProrationStrategy"" = 'FullAmount',
                    ""DueDayOfMonth"" = l.""DueDayOfMonth"",
                    ""EffectiveStartDate"" = l.""StartDate""
                FROM leases l
                WHERE l.""Id"" = lrc.""LeaseId"";
            ");

            // 2) one new BaseRent template row per existing lease, carrying forward its
            // MonthlyBaseRent/DueDayOfMonth before those columns are dropped below.
            migrationBuilder.Sql(@"
                INSERT INTO lease_recurring_charges (
                    ""Id"", ""TenantId"", ""LeaseId"", ""ChargeName"", ""Category"", ""Amount"",
                    ""RecurrencePattern"", ""RecurrenceInterval"", ""DueDayOfMonth"", ""EndStrategy"",
                    ""EffectiveStartDate"", ""ProrationStrategy"", ""IsPaused"", ""CreatedAt"")
                SELECT gen_random_uuid(), l.""TenantId"", l.""Id"", 'Base Rent', 'BaseRent', l.""MonthlyBaseRent"",
                       'Monthly', 1, l.""DueDayOfMonth"", 'LeaseAligned',
                       l.""StartDate"", 'FullAmount', false, now()
                FROM leases l;
            ");

            migrationBuilder.DropColumn(
                name: "DueDayOfMonth",
                table: "leases");

            migrationBuilder.DropColumn(
                name: "MonthlyBaseRent",
                table: "leases");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceRecurringChargeId",
                table: "charges",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_charges_SourceRecurringChargeId_DueDate",
                table: "charges",
                columns: new[] { "SourceRecurringChargeId", "DueDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_charges_SourceRecurringChargeId_DueDate",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "DueDayOfMonth",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "EffectiveEndDate",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "EffectiveStartDate",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "EndStrategy",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "IsPaused",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "ProrationStrategy",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "RecurrenceInterval",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "RecurrencePattern",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "SecondaryDueDay",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "TargetDayOfWeek",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "SourceRecurringChargeId",
                table: "charges");

            migrationBuilder.AddColumn<int>(
                name: "DueDayOfMonth",
                table: "leases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyBaseRent",
                table: "leases",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
