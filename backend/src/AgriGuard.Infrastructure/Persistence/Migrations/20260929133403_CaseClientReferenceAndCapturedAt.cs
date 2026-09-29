using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CaseClientReferenceAndCapturedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "captured_at",
                table: "crop_cases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "client_reference",
                table: "crop_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_crop_cases_farmer_id_client_reference",
                table: "crop_cases",
                columns: new[] { "farmer_id", "client_reference" },
                unique: true,
                filter: "client_reference IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_crop_cases_farmer_id_client_reference",
                table: "crop_cases");

            migrationBuilder.DropColumn(
                name: "captured_at",
                table: "crop_cases");

            migrationBuilder.DropColumn(
                name: "client_reference",
                table: "crop_cases");
        }
    }
}
