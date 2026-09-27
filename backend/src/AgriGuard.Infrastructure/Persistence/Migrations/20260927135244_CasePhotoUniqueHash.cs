using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CasePhotoUniqueHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_case_attachments_case_id",
                table: "case_attachments");

            migrationBuilder.CreateIndex(
                name: "ix_case_attachments_case_id_sha256",
                table: "case_attachments",
                columns: new[] { "case_id", "sha256" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_case_attachments_case_id_sha256",
                table: "case_attachments");

            migrationBuilder.CreateIndex(
                name: "ix_case_attachments_case_id",
                table: "case_attachments",
                column: "case_id");
        }
    }
}
