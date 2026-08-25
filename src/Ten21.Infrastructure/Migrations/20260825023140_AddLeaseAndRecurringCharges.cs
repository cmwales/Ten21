using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ten21.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaseAndRecurringCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MonthlyBaseRent = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DueDayOfMonth = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MoveOutNoticeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_leases_properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leases_resident_profiles_ResidentId",
                        column: x => x.ResidentId,
                        principalTable: "resident_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lease_recurring_charges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargeName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AccountingCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lease_recurring_charges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lease_recurring_charges_leases_LeaseId",
                        column: x => x.LeaseId,
                        principalTable: "leases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lease_recurring_charges_LeaseId",
                table: "lease_recurring_charges",
                column: "LeaseId");

            migrationBuilder.CreateIndex(
                name: "IX_lease_recurring_charges_TenantId",
                table: "lease_recurring_charges",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_leases_PropertyId",
                table: "leases",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_leases_ResidentId",
                table: "leases",
                column: "ResidentId");

            migrationBuilder.CreateIndex(
                name: "IX_leases_TenantId",
                table: "leases",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lease_recurring_charges");

            migrationBuilder.DropTable(
                name: "leases");
        }
    }
}
