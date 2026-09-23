using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CaseReferenceSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "case_reference_numbers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "case_reference_numbers");
        }
    }
}
