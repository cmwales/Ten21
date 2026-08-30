using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ten21.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditColumnsToAuditableEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "workspace_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "workspace_settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "workspace_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "unit_tiers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "unit_tiers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "unit_tiers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "unit_groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "unit_groups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "unit_groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "tenant_memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "tenant_memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "tenant_memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "security_deposits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "security_deposits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "security_deposits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "resident_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "resident_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "resident_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "refund_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "refund_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "refund_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "properties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "payment_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "payment_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "payment_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "payment_allocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "payment_allocations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "payment_allocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "leases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "leases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "leases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "lease_recurring_charges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "lease_recurring_charges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "lease_recurring_charges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "emergency_contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "emergency_contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "emergency_contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "deposit_settlement_allocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "deposit_settlement_allocations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "deposit_settlement_allocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "credit_allocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "credit_allocations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "credit_allocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "charges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "charges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "charges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "charge_adjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "charge_adjustments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "charge_adjustments",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "workspace_settings");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "workspace_settings");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "workspace_settings");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "unit_tiers");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "unit_tiers");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "unit_tiers");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "unit_groups");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "unit_groups");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "unit_groups");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "tenant_memberships");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "security_deposits");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "security_deposits");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "security_deposits");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "resident_profiles");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "resident_profiles");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "resident_profiles");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "refund_transactions");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "refund_transactions");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "refund_transactions");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "leases");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "leases");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "leases");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "lease_recurring_charges");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "emergency_contacts");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "emergency_contacts");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "emergency_contacts");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "deposit_settlement_allocations");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "deposit_settlement_allocations");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "deposit_settlement_allocations");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "credit_allocations");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "credit_allocations");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "credit_allocations");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "charge_adjustments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "charge_adjustments");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "charge_adjustments");
        }
    }
}
