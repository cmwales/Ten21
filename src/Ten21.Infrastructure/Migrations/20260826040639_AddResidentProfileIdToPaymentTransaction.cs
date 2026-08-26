using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ten21.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResidentProfileIdToPaymentTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ResidentProfileId",
                table: "payment_transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_ResidentProfileId",
                table: "payment_transactions",
                column: "ResidentProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_payment_transactions_resident_profiles_ResidentProfileId",
                table: "payment_transactions",
                column: "ResidentProfileId",
                principalTable: "resident_profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payment_transactions_resident_profiles_ResidentProfileId",
                table: "payment_transactions");

            migrationBuilder.DropIndex(
                name: "IX_payment_transactions_ResidentProfileId",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "ResidentProfileId",
                table: "payment_transactions");
        }
    }
}
