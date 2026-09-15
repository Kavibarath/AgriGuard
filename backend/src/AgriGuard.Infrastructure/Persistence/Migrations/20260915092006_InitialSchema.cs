using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "active_ingredients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    chemical_class = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resistance_group = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_active_ingredients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "crops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scientific_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    maturity_days = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crops", x => x.id);
                    table.CheckConstraint("ck_crops_maturity_days", "maturity_days BETWEEN 1 AND 730");
                });

            migrationBuilder.CreateTable(
                name: "districts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_districts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pathogens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    common_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    scientific_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    indicative_symptoms = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pathogens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "weather_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    latitude_rounded = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    longitude_rounded = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    fetched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    forecast_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weather_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    manufacturer = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    active_ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    formulation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    pack_size = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.CheckConstraint("ck_products_pack_size_positive", "pack_size > 0");
                    table.CheckConstraint("ck_products_unit_price_non_negative", "unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_products_active_ingredients_active_ingredient_id",
                        column: x => x.active_ingredient_id,
                        principalTable: "active_ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_centres",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    district_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    daily_capacity_kg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_centres", x => x.id);
                    table.CheckConstraint("ck_centres_capacity_positive", "daily_capacity_kg > 0");
                    table.ForeignKey(
                        name: "fk_collection_centres_districts_district_id",
                        column: x => x.district_id,
                        principalTable: "districts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    district_id = table.Column<Guid>(type: "uuid", nullable: true),
                    credit_limit = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_credit_limit", "credit_limit IS NULL OR credit_limit >= 0");
                    table.ForeignKey(
                        name: "fk_users_districts_district_id",
                        column: x => x.district_id,
                        principalTable: "districts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_crop_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    crop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_dose_per_hectare = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    max_dose_per_hectare = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    pre_harvest_interval_days = table.Column<int>(type: "integer", nullable: false),
                    re_entry_interval_hours = table.Column<int>(type: "integer", nullable: false),
                    max_applications_per_cycle = table.Column<int>(type: "integer", nullable: false),
                    min_days_between_applications = table.Column<int>(type: "integer", nullable: false),
                    rainfast_hours = table.Column<int>(type: "integer", nullable: false),
                    is_restricted = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_crop_approvals", x => x.id);
                    table.CheckConstraint("ck_approvals_dose_range", "min_dose_per_hectare <= max_dose_per_hectare");
                    table.CheckConstraint("ck_approvals_max_applications", "max_applications_per_cycle >= 1");
                    table.CheckConstraint("ck_approvals_min_dose_positive", "min_dose_per_hectare > 0");
                    table.CheckConstraint("ck_approvals_min_interval", "min_days_between_applications >= 0");
                    table.CheckConstraint("ck_approvals_phi_non_negative", "pre_harvest_interval_days >= 0");
                    table.CheckConstraint("ck_approvals_rainfast", "rainfast_hours >= 0");
                    table.CheckConstraint("ck_approvals_rei_non_negative", "re_entry_interval_hours >= 0");
                    table.ForeignKey(
                        name: "fk_product_crop_approvals_crops_crop_id",
                        column: x => x.crop_id,
                        principalTable: "crops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_crop_approvals_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_targets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pathogen_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_targets", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_targets_pathogens_pathogen_id",
                        column: x => x.pathogen_id,
                        principalTable: "pathogens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_targets_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "collection_slots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    centre_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    slot_index = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    capacity_kg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    booked_kg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_slots", x => x.id);
                    table.CheckConstraint("ck_slots_booked_non_negative", "booked_kg >= 0");
                    table.CheckConstraint("ck_slots_capacity_positive", "capacity_kg > 0");
                    table.CheckConstraint("ck_slots_no_overbooking", "booked_kg <= capacity_kg");
                    table.CheckConstraint("ck_slots_time_order", "end_time > start_time");
                    table.ForeignKey(
                        name: "fk_collection_slots_collection_centres_centre_id",
                        column: x => x.centre_id,
                        principalTable: "collection_centres",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dealers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    district_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dealers", x => x.id);
                    table.ForeignKey(
                        name: "fk_dealers_districts_district_id",
                        column: x => x.district_id,
                        principalTable: "districts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dealers_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "farms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    farmer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    village = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    district_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_farms", x => x.id);
                    table.ForeignKey(
                        name: "fk_farms_districts_district_id",
                        column: x => x.district_id,
                        principalTable: "districts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_farms_users_farmer_id",
                        column: x => x.farmer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dealer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    quantity_on_hand = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    quantity_reserved = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_batches", x => x.id);
                    table.CheckConstraint("ck_batches_on_hand_non_negative", "quantity_on_hand >= 0");
                    table.CheckConstraint("ck_batches_reserved_non_negative", "quantity_reserved >= 0");
                    table.CheckConstraint("ck_batches_reserved_within_on_hand", "quantity_reserved <= quantity_on_hand");
                    table.ForeignKey(
                        name: "fk_inventory_batches_dealers_dealer_id",
                        column: x => x.dealer_id,
                        principalTable: "dealers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_batches_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    farm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plot_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    area_hectares = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    soil_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plots", x => x.id);
                    table.CheckConstraint("ck_plots_area_positive", "area_hectares > 0");
                    table.CheckConstraint("ck_plots_latitude", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_plots_longitude", "longitude BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "fk_plots_farms_farm_id",
                        column: x => x.farm_id,
                        principalTable: "farms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "crop_cycles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    crop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sown_date = table.Column<DateOnly>(type: "date", nullable: false),
                    stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    expected_harvest_date = table.Column<DateOnly>(type: "date", nullable: false),
                    planned_harvest_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_harvest_date = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crop_cycles", x => x.id);
                    table.CheckConstraint("ck_crop_cycles_expected_after_sown", "expected_harvest_date >= sown_date");
                    table.CheckConstraint("ck_crop_cycles_planned_after_sown", "planned_harvest_date IS NULL OR planned_harvest_date >= sown_date");
                    table.ForeignKey(
                        name: "fk_crop_cycles_crops_crop_id",
                        column: x => x.crop_id,
                        principalTable: "crops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_crop_cycles_plots_plot_id",
                        column: x => x.plot_id,
                        principalTable: "plots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    slot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    crop_cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    farmer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_kg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    actual_quantity_kg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_quantity_positive", "quantity_kg > 0");
                    table.ForeignKey(
                        name: "fk_collection_bookings_collection_slots_slot_id",
                        column: x => x.slot_id,
                        principalTable: "collection_slots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_collection_bookings_crop_cycles_crop_cycle_id",
                        column: x => x.crop_cycle_id,
                        principalTable: "crop_cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_collection_bookings_users_farmer_id",
                        column: x => x.farmer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "crop_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    farmer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    crop_cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    district_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    severity = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    symptom_codes = table.Column<List<string>>(type: "text[]", nullable: false),
                    farmer_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reported_latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    reported_longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    confirmed_pathogen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_agronomist_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crop_cases", x => x.id);
                    table.ForeignKey(
                        name: "fk_crop_cases_crop_cycles_crop_cycle_id",
                        column: x => x.crop_cycle_id,
                        principalTable: "crop_cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_crop_cases_districts_district_id",
                        column: x => x.district_id,
                        principalTable: "districts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_crop_cases_pathogens_confirmed_pathogen_id",
                        column: x => x.confirmed_pathogen_id,
                        principalTable: "pathogens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_crop_cases_plots_plot_id",
                        column: x => x.plot_id,
                        principalTable: "plots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_crop_cases_users_assigned_agronomist_id",
                        column: x => x.assigned_agronomist_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_crop_cases_users_farmer_id",
                        column: x => x.farmer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "crop_stage_transitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    crop_cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    to_stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    transitioned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    transitioned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crop_stage_transitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_crop_stage_transitions_crop_cycles_crop_cycle_id",
                        column: x => x.crop_cycle_id,
                        principalTable: "crop_cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "harvest_forecasts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    crop_cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    forecast_harvest_date = table.Column<DateOnly>(type: "date", nullable: false),
                    estimated_yield_kg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    actual_yield_kg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_harvest_forecasts", x => x.id);
                    table.CheckConstraint("ck_forecasts_actual_yield", "actual_yield_kg IS NULL OR actual_yield_kg >= 0");
                    table.CheckConstraint("ck_forecasts_estimated_yield", "estimated_yield_kg >= 0");
                    table.ForeignKey(
                        name: "fk_harvest_forecasts_crop_cycles_crop_cycle_id",
                        column: x => x.crop_cycle_id,
                        principalTable: "crop_cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    objective = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    plan_json = table.Column<string>(type: "jsonb", nullable: true),
                    proposal_json = table.Column<string>(type: "jsonb", nullable: true),
                    verdict_json = table.Column<string>(type: "jsonb", nullable: true),
                    final_outcome_json = table.Column<string>(type: "jsonb", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    revision_count = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_runs", x => x.id);
                    table.CheckConstraint("ck_agent_runs_revision_count", "revision_count BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "fk_agent_runs_crop_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "crop_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "case_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_attachments", x => x.id);
                    table.CheckConstraint("ck_case_attachments_size", "size_bytes > 0 AND size_bytes <= 2097152");
                    table.ForeignKey(
                        name: "fk_case_attachments_crop_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "crop_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_run_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    agent_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    tool_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_run_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_run_events_agent_runs_agent_run_id",
                        column: x => x.agent_run_id,
                        principalTable: "agent_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_run_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    agent_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    goal = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    input_json = table.Column<string>(type: "jsonb", nullable: true),
                    output_json = table.Column<string>(type: "jsonb", nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_run_steps", x => x.id);
                    table.CheckConstraint("ck_agent_run_steps_retry_count", "retry_count >= 0");
                    table.ForeignKey(
                        name: "fk_agent_run_steps_agent_runs_agent_run_id",
                        column: x => x.agent_run_id,
                        principalTable: "agent_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "approval_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_approval_decisions_agent_runs_agent_run_id",
                        column: x => x.agent_run_id,
                        principalTable: "agent_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_approval_decisions_users_decided_by_user_id",
                        column: x => x.decided_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prescriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    prescription_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    crop_cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    diagnosed_pathogen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dose_per_hectare = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    total_quantity = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    spray_date = table.Column<DateOnly>(type: "date", nullable: false),
                    earliest_safe_harvest_date = table.Column<DateOnly>(type: "date", nullable: false),
                    instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    issued_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prescriptions", x => x.id);
                    table.CheckConstraint("ck_prescriptions_dose_positive", "dose_per_hectare > 0");
                    table.CheckConstraint("ck_prescriptions_quantity_positive", "total_quantity > 0");
                    table.CheckConstraint("ck_prescriptions_safe_harvest_after_spray", "earliest_safe_harvest_date >= spray_date");
                    table.ForeignKey(
                        name: "fk_prescriptions_agent_runs_agent_run_id",
                        column: x => x.agent_run_id,
                        principalTable: "agent_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_prescriptions_crop_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "crop_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_prescriptions_crop_cycles_crop_cycle_id",
                        column: x => x.crop_cycle_id,
                        principalTable: "crop_cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_prescriptions_pathogens_diagnosed_pathogen_id",
                        column: x => x.diagnosed_pathogen_id,
                        principalTable: "pathogens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_prescriptions_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_prescriptions_users_issued_by_user_id",
                        column: x => x.issued_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dealer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_quantity = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_reservations", x => x.id);
                    table.CheckConstraint("ck_reservations_quantity_positive", "total_quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_reservations_agent_runs_agent_run_id",
                        column: x => x.agent_run_id,
                        principalTable: "agent_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_reservations_dealers_dealer_id",
                        column: x => x.dealer_id,
                        principalTable: "dealers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_reservations_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chemical_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    crop_cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prescription_id = table.Column<Guid>(type: "uuid", nullable: true),
                    application_date = table.Column<DateOnly>(type: "date", nullable: false),
                    dose_per_hectare = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    total_quantity = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chemical_applications", x => x.id);
                    table.CheckConstraint("ck_chemical_applications_dose_positive", "dose_per_hectare > 0");
                    table.CheckConstraint("ck_chemical_applications_quantity_positive", "total_quantity > 0");
                    table.ForeignKey(
                        name: "fk_chemical_applications_crop_cycles_crop_cycle_id",
                        column: x => x.crop_cycle_id,
                        principalTable: "crop_cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chemical_applications_prescriptions_prescription_id",
                        column: x => x.prescription_id,
                        principalTable: "prescriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chemical_applications_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "input_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    farmer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dealer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prescription_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    collected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_input_orders", x => x.id);
                    table.CheckConstraint("ck_orders_total_non_negative", "total_amount >= 0");
                    table.ForeignKey(
                        name: "fk_input_orders_dealers_dealer_id",
                        column: x => x.dealer_id,
                        principalTable: "dealers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_input_orders_prescriptions_prescription_id",
                        column: x => x.prescription_id,
                        principalTable: "prescriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_input_orders_users_farmer_id",
                        column: x => x.farmer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_reservation_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_reservation_lines", x => x.id);
                    table.CheckConstraint("ck_reservation_lines_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_reservation_lines_inventory_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "inventory_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_reservation_lines_stock_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalTable: "stock_reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "input_order_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    packs = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_input_order_lines", x => x.id);
                    table.CheckConstraint("ck_order_lines_packs_positive", "packs > 0");
                    table.CheckConstraint("ck_order_lines_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_input_order_lines_input_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "input_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_input_order_lines_inventory_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "inventory_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_input_order_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_active_ingredients_name",
                table: "active_ingredients",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_run_events_agent_run_id_occurred_at",
                table: "agent_run_events",
                columns: new[] { "agent_run_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_run_steps_agent_run_id_sequence_no",
                table: "agent_run_steps",
                columns: new[] { "agent_run_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_case_id_created_at",
                table: "agent_runs",
                columns: new[] { "case_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_status",
                table: "agent_runs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_approval_decisions_agent_run_id_decided_at",
                table: "approval_decisions",
                columns: new[] { "agent_run_id", "decided_at" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_decisions_decided_by_user_id",
                table: "approval_decisions",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_decisions_idempotency_key",
                table: "approval_decisions",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_case_attachments_case_id",
                table: "case_attachments",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_chemical_applications_crop_cycle_id_product_id_application_",
                table: "chemical_applications",
                columns: new[] { "crop_cycle_id", "product_id", "application_date" });

            migrationBuilder.CreateIndex(
                name: "ix_chemical_applications_prescription_id",
                table: "chemical_applications",
                column: "prescription_id");

            migrationBuilder.CreateIndex(
                name: "ix_chemical_applications_product_id",
                table: "chemical_applications",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_bookings_booking_no",
                table: "collection_bookings",
                column: "booking_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collection_bookings_crop_cycle_id",
                table: "collection_bookings",
                column: "crop_cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_bookings_farmer_id",
                table: "collection_bookings",
                column: "farmer_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_bookings_slot_id",
                table: "collection_bookings",
                column: "slot_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_centres_district_id",
                table: "collection_centres",
                column: "district_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_centres_name",
                table: "collection_centres",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collection_slots_centre_id_slot_date_slot_index",
                table: "collection_slots",
                columns: new[] { "centre_id", "slot_date", "slot_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collection_slots_slot_date",
                table: "collection_slots",
                column: "slot_date");

            migrationBuilder.CreateIndex(
                name: "ix_crop_cases_assigned_agronomist_id",
                table: "crop_cases",
                column: "assigned_agronomist_id");

            migrationBuilder.CreateIndex(
                name: "ix_crop_cases_confirmed_pathogen_id",
                table: "crop_cases",
                column: "confirmed_pathogen_id");

            migrationBuilder.CreateIndex(
                name: "ix_crop_cases_crop_cycle_id",
                table: "crop_cases",
                column: "crop_cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_crop_cases_district_id_confirmed_pathogen_id_created_at",
                table: "crop_cases",
                columns: new[] { "district_id", "confirmed_pathogen_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_crop_cases_farmer_id",
                table: "crop_cases",
                column: "farmer_id");

            migrationBuilder.CreateIndex(
                name: "ix_crop_cases_plot_id",
                table: "crop_cases",
                column: "plot_id");

            migrationBuilder.CreateIndex(
                name: "ix_crop_cases_reference_no",
                table: "crop_cases",
                column: "reference_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_crop_cases_status_district_id_created_at",
                table: "crop_cases",
                columns: new[] { "status", "district_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_crop_cases_symptom_codes",
                table: "crop_cases",
                column: "symptom_codes")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_crop_cycles_crop_id",
                table: "crop_cycles",
                column: "crop_id");

            migrationBuilder.CreateIndex(
                name: "ix_crop_cycles_plot_id_status",
                table: "crop_cycles",
                columns: new[] { "plot_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_crop_cycles_one_active_per_plot",
                table: "crop_cycles",
                column: "plot_id",
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_crop_stage_transitions_crop_cycle_id_transitioned_at",
                table: "crop_stage_transitions",
                columns: new[] { "crop_cycle_id", "transitioned_at" });

            migrationBuilder.CreateIndex(
                name: "ix_crops_code",
                table: "crops",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dealers_district_id",
                table: "dealers",
                column: "district_id");

            migrationBuilder.CreateIndex(
                name: "ix_dealers_user_id",
                table: "dealers",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_districts_code",
                table: "districts",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_farms_district_id",
                table: "farms",
                column: "district_id");

            migrationBuilder.CreateIndex(
                name: "ix_farms_farmer_id",
                table: "farms",
                column: "farmer_id");

            migrationBuilder.CreateIndex(
                name: "ix_harvest_forecasts_crop_cycle_id_created_at",
                table: "harvest_forecasts",
                columns: new[] { "crop_cycle_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_input_order_lines_batch_id",
                table: "input_order_lines",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_input_order_lines_order_id",
                table: "input_order_lines",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_input_order_lines_product_id",
                table: "input_order_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_input_orders_dealer_id_status",
                table: "input_orders",
                columns: new[] { "dealer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_input_orders_farmer_id",
                table: "input_orders",
                column: "farmer_id");

            migrationBuilder.CreateIndex(
                name: "ix_input_orders_order_no",
                table: "input_orders",
                column: "order_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_input_orders_prescription_id",
                table: "input_orders",
                column: "prescription_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_dealer_id_product_id_batch_no",
                table: "inventory_batches",
                columns: new[] { "dealer_id", "product_id", "batch_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_in_stock",
                table: "inventory_batches",
                columns: new[] { "product_id", "dealer_id", "expiry_date" },
                filter: "quantity_on_hand > 0");

            migrationBuilder.CreateIndex(
                name: "ix_pathogens_code",
                table: "pathogens",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plots_farm_id_plot_code",
                table: "plots",
                columns: new[] { "farm_id", "plot_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_agent_run_id",
                table: "prescriptions",
                column: "agent_run_id",
                unique: true,
                filter: "agent_run_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_case_id",
                table: "prescriptions",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_crop_cycle_id",
                table: "prescriptions",
                column: "crop_cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_diagnosed_pathogen_id",
                table: "prescriptions",
                column: "diagnosed_pathogen_id");

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_issued_by_user_id",
                table: "prescriptions",
                column: "issued_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_prescription_no",
                table: "prescriptions",
                column: "prescription_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_product_id",
                table: "prescriptions",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_crop_approvals_crop_id_is_active",
                table: "product_crop_approvals",
                columns: new[] { "crop_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_product_crop_approvals_product_id_crop_id",
                table: "product_crop_approvals",
                columns: new[] { "product_id", "crop_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_targets_pathogen_id",
                table: "product_targets",
                column: "pathogen_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_targets_product_id_pathogen_id",
                table: "product_targets",
                columns: new[] { "product_id", "pathogen_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_active_ingredient_id",
                table: "products",
                column: "active_ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_name",
                table: "products",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservation_lines_batch_id",
                table: "stock_reservation_lines",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservation_lines_reservation_id_batch_id",
                table: "stock_reservation_lines",
                columns: new[] { "reservation_id", "batch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_agent_run_id",
                table: "stock_reservations",
                column: "agent_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_dealer_id",
                table: "stock_reservations",
                column: "dealer_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_product_id",
                table: "stock_reservations",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_status_expires_at",
                table: "stock_reservations",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_users_district_id",
                table: "users",
                column: "district_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_role",
                table: "users",
                column: "role");

            migrationBuilder.CreateIndex(
                name: "ix_weather_snapshots_expires_at",
                table: "weather_snapshots",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_weather_snapshots_latitude_rounded_longitude_rounded",
                table: "weather_snapshots",
                columns: new[] { "latitude_rounded", "longitude_rounded" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_run_events");

            migrationBuilder.DropTable(
                name: "agent_run_steps");

            migrationBuilder.DropTable(
                name: "approval_decisions");

            migrationBuilder.DropTable(
                name: "case_attachments");

            migrationBuilder.DropTable(
                name: "chemical_applications");

            migrationBuilder.DropTable(
                name: "collection_bookings");

            migrationBuilder.DropTable(
                name: "crop_stage_transitions");

            migrationBuilder.DropTable(
                name: "harvest_forecasts");

            migrationBuilder.DropTable(
                name: "input_order_lines");

            migrationBuilder.DropTable(
                name: "product_crop_approvals");

            migrationBuilder.DropTable(
                name: "product_targets");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "stock_reservation_lines");

            migrationBuilder.DropTable(
                name: "weather_snapshots");

            migrationBuilder.DropTable(
                name: "collection_slots");

            migrationBuilder.DropTable(
                name: "input_orders");

            migrationBuilder.DropTable(
                name: "inventory_batches");

            migrationBuilder.DropTable(
                name: "stock_reservations");

            migrationBuilder.DropTable(
                name: "collection_centres");

            migrationBuilder.DropTable(
                name: "prescriptions");

            migrationBuilder.DropTable(
                name: "dealers");

            migrationBuilder.DropTable(
                name: "agent_runs");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "crop_cases");

            migrationBuilder.DropTable(
                name: "active_ingredients");

            migrationBuilder.DropTable(
                name: "crop_cycles");

            migrationBuilder.DropTable(
                name: "pathogens");

            migrationBuilder.DropTable(
                name: "crops");

            migrationBuilder.DropTable(
                name: "plots");

            migrationBuilder.DropTable(
                name: "farms");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "districts");
        }
    }
}
