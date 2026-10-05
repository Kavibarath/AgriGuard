using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "paid_at",
                table: "input_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_status",
                table: "input_orders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Unpaid");

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_payment_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    checkout_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    card_brand = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    card_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    refund_due = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_payments_card_has_provider", "method <> 'Card' OR provider <> 'counter'");
                    table.ForeignKey(
                        name: "fk_payments_input_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "input_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_users_recorded_by_id",
                        column: x => x.recorded_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_order_id_status",
                table: "payments",
                columns: new[] { "order_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_provider_reference",
                table: "payments",
                column: "provider_reference",
                unique: true,
                filter: "provider_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payments_recorded_by_id",
                table: "payments",
                column: "recorded_by_id");

            migrationBuilder.CreateIndex(
                name: "ux_payments_one_pending_card_per_order",
                table: "payments",
                column: "order_id",
                unique: true,
                filter: "status = 'Pending' AND method = 'Card'");

            // Orders handed over before payments were recorded were settled at the counter; they
            // must not now read "Unpaid". They have no payment row, so they show no method.
            migrationBuilder.Sql("UPDATE input_orders SET payment_status = 'Paid', paid_at = collected_at WHERE status = 'Collected';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropColumn(
                name: "paid_at",
                table: "input_orders");

            migrationBuilder.DropColumn(
                name: "payment_status",
                table: "input_orders");
        }
    }
}
