using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ten21.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResidentUserIdAndPropertyDuplicateDetectionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_resident_profiles_UserId",
                table: "resident_profiles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_properties_StreetAddress1_City_State_PostalCode_Country_Uni~",
                table: "properties",
                columns: new[] { "StreetAddress1", "City", "State", "PostalCode", "Country", "UnitIdentifier" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_resident_profiles_UserId",
                table: "resident_profiles");

            migrationBuilder.DropIndex(
                name: "IX_properties_StreetAddress1_City_State_PostalCode_Country_Uni~",
                table: "properties");
        }
    }
}
