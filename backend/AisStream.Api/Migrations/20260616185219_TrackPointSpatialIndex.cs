using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AisStream.Api.Migrations
{
    /// <inheritdoc />
    public partial class TrackPointSpatialIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TrackPoints_Location",
                table: "TrackPoints",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrackPoints_Location",
                table: "TrackPoints");
        }
    }
}
