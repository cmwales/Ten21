using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ten21.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditAllocationsAndRefundTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnallocatedAmount",
                table: "payment_transactions",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "charge_adjustments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250);

            migrationBuilder.CreateTable(
                name: "credit_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePaymentTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetChargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AppliedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_credit_allocations_charges_TargetChargeId",
                        column: x => x.TargetChargeId,
                        principalTable: "charges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_allocations_payment_transactions_SourcePaymentTransa~",
                        column: x => x.SourcePaymentTransactionId,
                        principalTable: "payment_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refund_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RefundDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TenderType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refund_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refund_transactions_properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_refund_transactions_resident_profiles_ResidentProfileId",
                        column: x => x.ResidentProfileId,
                        principalTable: "resident_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_credit_allocations_SourcePaymentTransactionId",
                table: "credit_allocations",
                column: "SourcePaymentTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_allocations_TargetChargeId",
                table: "credit_allocations",
                column: "TargetChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_allocations_TenantId",
                table: "credit_allocations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_refund_transactions_PropertyId",
                table: "refund_transactions",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_refund_transactions_ResidentProfileId",
                table: "refund_transactions",
                column: "ResidentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_refund_transactions_TenantId",
                table: "refund_transactions",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_allocations");

            migrationBuilder.DropTable(
                name: "refund_transactions");

            migrationBuilder.DropColumn(
                name: "UnallocatedAmount",
                table: "payment_transactions");

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "charge_adjustments",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);
        }
    }
}
