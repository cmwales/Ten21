using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ten21.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityDepositsAndSettlementAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "security_deposits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AmountHeld = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CollectedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_deposits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_security_deposits_properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_security_deposits_resident_profiles_ResidentProfileId",
                        column: x => x.ResidentProfileId,
                        principalTable: "resident_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deposit_settlement_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecurityDepositId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetChargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AppliedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deposit_settlement_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_deposit_settlement_allocations_charges_TargetChargeId",
                        column: x => x.TargetChargeId,
                        principalTable: "charges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deposit_settlement_allocations_security_deposits_SecurityDe~",
                        column: x => x.SecurityDepositId,
                        principalTable: "security_deposits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_deposit_settlement_allocations_SecurityDepositId",
                table: "deposit_settlement_allocations",
                column: "SecurityDepositId");

            migrationBuilder.CreateIndex(
                name: "IX_deposit_settlement_allocations_TargetChargeId",
                table: "deposit_settlement_allocations",
                column: "TargetChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_deposit_settlement_allocations_TenantId",
                table: "deposit_settlement_allocations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_security_deposits_PropertyId",
                table: "security_deposits",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_security_deposits_ResidentProfileId",
                table: "security_deposits",
                column: "ResidentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_security_deposits_TenantId",
                table: "security_deposits",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deposit_settlement_allocations");

            migrationBuilder.DropTable(
                name: "security_deposits");
        }
    }
}
