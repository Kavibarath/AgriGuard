using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InputOrderPickupCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pickup_code",
                table: "input_orders",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            // Orders not yet collected get a code too, so they can still be handed over. Collected
            // and cancelled ones keep none: there is nothing left to hand over.
            migrationBuilder.Sql(
                "UPDATE input_orders SET pickup_code = lpad(floor(random() * 1000000)::int::text, 6, '0') " +
                "WHERE pickup_code IS NULL AND status NOT IN ('Collected', 'Cancelled');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pickup_code",
                table: "input_orders");
        }
    }
}
