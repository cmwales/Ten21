using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ten21.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyUnitSprint3Fields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StreetAddress",
                table: "properties",
                newName: "StreetAddress1");

            migrationBuilder.RenameColumn(
                name: "StateProvince",
                table: "properties",
                newName: "State");

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "properties",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultTargetRent",
                table: "properties",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "properties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PropertyType",
                table: "properties",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StreetAddress2",
                table: "properties",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitIdentifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TargetRent = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    OccupancyStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "units");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "DefaultTargetRent",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "PropertyType",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "StreetAddress2",
                table: "properties");

            migrationBuilder.RenameColumn(
                name: "StreetAddress1",
                table: "properties",
                newName: "StreetAddress");

            migrationBuilder.RenameColumn(
                name: "State",
                table: "properties",
                newName: "StateProvince");
        }
    }
}
