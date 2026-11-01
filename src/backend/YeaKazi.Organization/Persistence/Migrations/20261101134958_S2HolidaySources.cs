using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YeaKazi.Organization.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class S2HolidaySources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "rule_version",
                schema: "org",
                table: "holidays",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_reference",
                schema: "org",
                table: "holidays",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rule_version",
                schema: "org",
                table: "holidays");

            migrationBuilder.DropColumn(
                name: "source_reference",
                schema: "org",
                table: "holidays");
        }
    }
}
