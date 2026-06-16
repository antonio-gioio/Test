using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AisStream.Api.Migrations
{
    /// <inheritdoc />
    public partial class VesselStaticData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Draught",
                table: "Vessels",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Eta",
                table: "Vessels",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Imo",
                table: "Vessels",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Length",
                table: "Vessels",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Width",
                table: "Vessels",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Draught",
                table: "Vessels");

            migrationBuilder.DropColumn(
                name: "Eta",
                table: "Vessels");

            migrationBuilder.DropColumn(
                name: "Imo",
                table: "Vessels");

            migrationBuilder.DropColumn(
                name: "Length",
                table: "Vessels");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "Vessels");
        }
    }
}
