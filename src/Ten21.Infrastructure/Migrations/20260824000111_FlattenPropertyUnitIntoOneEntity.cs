using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ten21.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FlattenPropertyUnitIntoOneEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "units");

            migrationBuilder.RenameColumn(
                name: "DefaultTargetRent",
                table: "properties",
                newName: "TargetRent");

            migrationBuilder.AddColumn<string>(
                name: "OccupancyStatus",
                table: "properties",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UnitIdentifier",
                table: "properties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OccupancyStatus",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "UnitIdentifier",
                table: "properties");

            migrationBuilder.RenameColumn(
                name: "TargetRent",
                table: "properties",
                newName: "DefaultTargetRent");

            migrationBuilder.CreateTable(
                name: "units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    OccupancyStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetRent = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitIdentifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_units_properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_units_PropertyId",
                table: "units",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_units_TenantId",
                table: "units",
                column: "TenantId");
        }
    }
}
