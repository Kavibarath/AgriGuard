using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CollectionBookingSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "collection_booking_numbers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "collection_booking_numbers");
        }
    }
}
