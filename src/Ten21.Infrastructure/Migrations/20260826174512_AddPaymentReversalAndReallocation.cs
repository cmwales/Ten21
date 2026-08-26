using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ten21.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentReversalAndReallocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReallocatedToId",
                table: "payment_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReversalReason",
                table: "payment_transactions",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "payment_transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Cleared");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_ReallocatedToId",
                table: "payment_transactions",
                column: "ReallocatedToId");

            migrationBuilder.AddForeignKey(
                name: "FK_payment_transactions_payment_transactions_ReallocatedToId",
                table: "payment_transactions",
                column: "ReallocatedToId",
                principalTable: "payment_transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payment_transactions_payment_transactions_ReallocatedToId",
                table: "payment_transactions");

            migrationBuilder.DropIndex(
                name: "IX_payment_transactions_ReallocatedToId",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "ReallocatedToId",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "ReversalReason",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "payment_transactions");
        }
    }
}
