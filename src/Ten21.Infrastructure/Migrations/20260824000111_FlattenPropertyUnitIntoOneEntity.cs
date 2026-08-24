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
            // Columns are added -- with a real, valid default -- BEFORE "units" is dropped
            // below, so the data-preservation SQL that follows can still read from "units"
            // while populating them. The original ordering here (drop table first, then add
            // columns with defaultValue: "") was wrong on two counts: it discarded every
            // pre-existing Unit row with no data-preserving step, and it backfilled
            // OccupancyStatus with "" -- not a valid OccupancyStatus enum member -- which
            // would throw the moment EF Core's string-to-enum converter tried to read any
            // pre-existing Property row back out.
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
                defaultValue: "Vacant");

            migrationBuilder.AddColumn<string>(
                name: "UnitIdentifier",
                table: "properties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Data preservation: "each place that can be rented should represent a
            // property" -- a duplex, or any building that had child Units, must come out of
            // this migration as one independent Property row per rentable half/suite, not
            // collapsed into a single row. A Property that had child Units was itself just a
            // container (the leasable spaces were its Units, never the parent row directly),
            // so its own row is repurposed to represent its FIRST unit -- same Id, nothing
            // else references it yet -- and one new Property row is inserted for every
            // additional unit. A Property with zero units was already its own rentable space
            // (e.g. a standalone single-family house) and keeps its row exactly as-is; the
            // AddColumn defaults above already gave it OccupancyStatus = Vacant and
            // UnitIdentifier = null, and TargetRent survived via the RenameColumn.
            migrationBuilder.Sql(@"
                WITH first_unit AS (
                    SELECT DISTINCT ON (u.""PropertyId"")
                        u.""Id"" AS unit_id, u.""PropertyId"", u.""UnitIdentifier"",
                        u.""TargetRent"", u.""OccupancyStatus""
                    FROM units u
                    ORDER BY u.""PropertyId"", u.""Id""
                )
                UPDATE properties p
                SET ""UnitIdentifier"" = fu.""UnitIdentifier"",
                    ""TargetRent"" = fu.""TargetRent"",
                    ""OccupancyStatus"" = fu.""OccupancyStatus""
                FROM first_unit fu
                WHERE p.""Id"" = fu.""PropertyId"";
            ");

            migrationBuilder.Sql(@"
                WITH first_unit AS (
                    SELECT DISTINCT ON (u.""PropertyId"") u.""Id"" AS unit_id
                    FROM units u
                    ORDER BY u.""PropertyId"", u.""Id""
                )
                INSERT INTO properties (
                    ""Id"", ""TenantId"", ""Name"", ""PropertyType"", ""StreetAddress1"",
                    ""StreetAddress2"", ""City"", ""State"", ""PostalCode"", ""Country"",
                    ""UnitIdentifier"", ""TargetRent"", ""OccupancyStatus"", ""CreatedAt"",
                    ""IsDeleted""
                )
                SELECT gen_random_uuid(), p.""TenantId"", p.""Name"", p.""PropertyType"",
                       p.""StreetAddress1"", p.""StreetAddress2"", p.""City"", p.""State"",
                       p.""PostalCode"", p.""Country"", u.""UnitIdentifier"", u.""TargetRent"",
                       u.""OccupancyStatus"", u.""CreatedAt"", u.""IsDeleted""
                FROM units u
                JOIN properties p ON p.""Id"" = u.""PropertyId""
                LEFT JOIN first_unit fu ON fu.unit_id = u.""Id""
                WHERE fu.unit_id IS NULL;
            ");

            migrationBuilder.DropTable(
                name: "units");
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
