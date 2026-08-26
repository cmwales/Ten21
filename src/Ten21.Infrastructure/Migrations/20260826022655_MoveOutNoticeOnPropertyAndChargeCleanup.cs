using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ten21.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveOutNoticeOnPropertyAndChargeCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_manual_charges_resident_profiles_ResidentId",
                table: "manual_charges");

            migrationBuilder.DropIndex(
                name: "IX_manual_charges_ResidentId",
                table: "manual_charges");

            migrationBuilder.DropColumn(
                name: "ResidentId",
                table: "manual_charges");

            migrationBuilder.DropColumn(
                name: "MoveOutNoticeDate",
                table: "leases");

            migrationBuilder.AddColumn<DateOnly>(
                name: "MoveOutNoticeDate",
                table: "properties",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PaidDate",
                table: "manual_charges",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MoveOutNoticeDate",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "PaidDate",
                table: "manual_charges");

            migrationBuilder.AddColumn<Guid>(
                name: "ResidentId",
                table: "manual_charges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MoveOutNoticeDate",
                table: "leases",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_manual_charges_ResidentId",
                table: "manual_charges",
                column: "ResidentId");

            migrationBuilder.AddForeignKey(
                name: "FK_manual_charges_resident_profiles_ResidentId",
                table: "manual_charges",
                column: "ResidentId",
                principalTable: "resident_profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
