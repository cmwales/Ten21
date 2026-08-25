using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ten21.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitTiersAndGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UnitGroupId",
                table: "properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitTierId",
                table: "properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "unit_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unit_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "unit_tiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TierName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DefaultRent = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AccountingCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unit_tiers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_properties_UnitGroupId",
                table: "properties",
                column: "UnitGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_properties_UnitTierId",
                table: "properties",
                column: "UnitTierId");

            migrationBuilder.CreateIndex(
                name: "IX_unit_groups_TenantId",
                table: "unit_groups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_unit_tiers_TenantId",
                table: "unit_tiers",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_properties_unit_groups_UnitGroupId",
                table: "properties",
                column: "UnitGroupId",
                principalTable: "unit_groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_properties_unit_tiers_UnitTierId",
                table: "properties",
                column: "UnitTierId",
                principalTable: "unit_tiers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_properties_unit_groups_UnitGroupId",
                table: "properties");

            migrationBuilder.DropForeignKey(
                name: "FK_properties_unit_tiers_UnitTierId",
                table: "properties");

            migrationBuilder.DropTable(
                name: "unit_groups");

            migrationBuilder.DropTable(
                name: "unit_tiers");

            migrationBuilder.DropIndex(
                name: "IX_properties_UnitGroupId",
                table: "properties");

            migrationBuilder.DropIndex(
                name: "IX_properties_UnitTierId",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "UnitGroupId",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "UnitTierId",
                table: "properties");
        }
    }
}
