using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Shipment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "compliance");

            migrationBuilder.EnsureSchema(name: "carriers");

            migrationBuilder.EnsureSchema(name: "crm");

            migrationBuilder.EnsureSchema(name: "ordering");

            migrationBuilder.EnsureSchema(name: "billing");

            migrationBuilder.EnsureSchema(name: "messaging");

            migrationBuilder.EnsureSchema(name: "tracking");

            migrationBuilder.EnsureSchema(name: "catalog");

            migrationBuilder.EnsureSchema(name: "dispatch");

            migrationBuilder.EnsureSchema(name: "identity");

            migrationBuilder.CreateSequence(
                name: "order_code_seq",
                schema: "ordering",
                startValue: 104001L
            );

            migrationBuilder.CreateSequence(
                name: "shipment_code_seq",
                schema: "dispatch",
                startValue: 204001L
            );

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "crm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    email = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    phone = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: true
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "delivery_fee_bands",
                schema: "ordering",
                columns: table => new
                {
                    max_distance_m = table.Column<int>(type: "integer", nullable: false),
                    fee_minor = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_fee_bands", x => x.max_distance_m);
                }
            );

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    processed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "partners",
                schema: "crm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(
                        type: "character varying(12)",
                        maxLength: 12,
                        nullable: false
                    ),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    kind = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    commission_bps = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partners", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "processed_events",
                schema: "billing",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    processed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_events", x => x.event_id);
                }
            );

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(
                        type: "uuid",
                        nullable: false,
                        defaultValueSql: "uuidv7()"
                    ),
                    username = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    email = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    password_hash = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    display_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    role = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false,
                        defaultValue: "admin"
                    ),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false,
                        defaultValue: "active"
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "vehicle_types",
                schema: "carriers",
                columns: table => new
                {
                    code = table.Column<string>(
                        type: "character varying(10)",
                        maxLength: 10,
                        nullable: false
                    ),
                    name = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    max_load_g = table.Column<int>(type: "integer", nullable: false),
                    max_size_class = table.Column<string>(
                        type: "character varying(2)",
                        maxLength: 2,
                        nullable: false
                    ),
                    max_distance_m = table.Column<int>(type: "integer", nullable: false),
                    avg_speed_kmh = table.Column<int>(type: "integer", nullable: false),
                    needs_licence = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_types", x => x.code);
                }
            );

            migrationBuilder.CreateTable(
                name: "customer_addresses",
                schema: "crm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    address_line = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    city = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    postcode = table.Column<string>(
                        type: "character varying(10)",
                        maxLength: 10,
                        nullable: false
                    ),
                    latitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: false
                    ),
                    longitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: false
                    ),
                    notes = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_addresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_addresses_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "crm",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "pickup_points",
                schema: "crm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    address_line = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    city = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    postcode = table.Column<string>(
                        type: "character varying(10)",
                        maxLength: 10,
                        nullable: false
                    ),
                    latitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: false
                    ),
                    longitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: false
                    ),
                    opens_minute = table.Column<short>(type: "smallint", nullable: false),
                    closes_minute = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pickup_points", x => x.id);
                    table.ForeignKey(
                        name: "fk_pickup_points_partners_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "crm",
                        principalTable: "partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "products",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    category = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    description = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: true
                    ),
                    price_minor = table.Column<long>(type: "bigint", nullable: false),
                    weight_g = table.Column<int>(type: "integer", nullable: false),
                    size_class = table.Column<string>(
                        type: "character varying(2)",
                        maxLength: 2,
                        nullable: false
                    ),
                    age_restricted = table.Column<bool>(type: "boolean", nullable: false),
                    listed_available = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: true
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_products_partners_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "crm",
                        principalTable: "partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "couriers",
                schema: "carriers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    phone = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: false
                    ),
                    email = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    vehicle_type = table.Column<string>(
                        type: "character varying(10)",
                        maxLength: 10,
                        nullable: false
                    ),
                    vehicle_plate = table.Column<string>(
                        type: "character varying(15)",
                        maxLength: 15,
                        nullable: true
                    ),
                    shift_status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    shift_started_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    last_latitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: true
                    ),
                    last_longitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: true
                    ),
                    last_position_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_couriers", x => x.id);
                    table.ForeignKey(
                        name: "fk_couriers_vehicle_types_vehicle_type",
                        column: x => x.vehicle_type,
                        principalSchema: "carriers",
                        principalTable: "vehicle_types",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "payout_rates",
                schema: "billing",
                columns: table => new
                {
                    vehicle_type = table.Column<string>(
                        type: "character varying(10)",
                        maxLength: 10,
                        nullable: false
                    ),
                    base_minor = table.Column<long>(type: "bigint", nullable: false),
                    per_km_minor = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payout_rates", x => x.vehicle_type);
                    table.ForeignKey(
                        name: "fk_payout_rates_vehicle_types_vehicle_type",
                        column: x => x.vehicle_type,
                        principalSchema: "carriers",
                        principalTable: "vehicle_types",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "ordering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(
                        type: "character varying(12)",
                        maxLength: 12,
                        nullable: false
                    ),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    address_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_label = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    address_line = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    address_city = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    address_postcode = table.Column<string>(
                        type: "character varying(10)",
                        maxLength: 10,
                        nullable: false
                    ),
                    address_notes = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    address_latitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: false
                    ),
                    address_longitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: false
                    ),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    pickup_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pickup_point_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    pickup_address_line = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    pickup_latitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: false
                    ),
                    pickup_longitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: false
                    ),
                    distance_m = table.Column<int>(type: "integer", nullable: false),
                    delivery_fee_minor = table.Column<long>(type: "bigint", nullable: false),
                    goods_ordered_minor = table.Column<long>(type: "bigint", nullable: false),
                    goods_picked_minor = table.Column<long>(type: "bigint", nullable: true),
                    tip_percent = table.Column<short>(type: "smallint", nullable: false),
                    tip_minor = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    cancel_reason = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    failure_reason = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    picked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    placed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    picking_started_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ready_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    cancelled_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    delivered_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    failed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_orders_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "crm",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "fk_orders_partners_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "crm",
                        principalTable: "partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "fk_orders_pickup_points_pickup_point_id",
                        column: x => x.pickup_point_id,
                        principalSchema: "crm",
                        principalTable: "pickup_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "courier_documents",
                schema: "compliance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: false
                    ),
                    number = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_courier_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_courier_documents_couriers_courier_id",
                        column: x => x.courier_id,
                        principalSchema: "carriers",
                        principalTable: "couriers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "entries",
                schema: "billing",
                columns: table => new
                {
                    id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityAlwaysColumn
                        ),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_code = table.Column<string>(
                        type: "character varying(12)",
                        maxLength: 12,
                        nullable: false
                    ),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    courier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    line_no = table.Column<int>(type: "integer", nullable: true),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: true),
                    basis = table.Column<long>(type: "bigint", nullable: true),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    booked_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    description = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_entries_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "ordering",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "fk_entries_processed_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "billing",
                        principalTable: "processed_events",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "order_lines",
                schema: "ordering",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    unit_price_minor = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    weight_g = table.Column<int>(type: "integer", nullable: false),
                    size_class = table.Column<string>(
                        type: "character varying(2)",
                        maxLength: 2,
                        nullable: false
                    ),
                    age_restricted = table.Column<bool>(type: "boolean", nullable: false),
                    picked_quantity = table.Column<int>(type: "integer", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_lines", x => new { x.order_id, x.line_no });
                    table.ForeignKey(
                        name: "fk_order_lines_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "ordering",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "fk_order_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "dispatch",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(
                        type: "character varying(12)",
                        maxLength: 12,
                        nullable: false
                    ),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_code = table.Column<string>(
                        type: "character varying(12)",
                        maxLength: 12,
                        nullable: false
                    ),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    partner_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    pickup_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pickup_point_name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    pickup_address_line = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    pickup_latitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: false
                    ),
                    pickup_longitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: false
                    ),
                    dropoff_label = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    dropoff_address_line = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    dropoff_city = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    dropoff_notes = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    dropoff_latitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: false
                    ),
                    dropoff_longitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: false
                    ),
                    distance_m = table.Column<int>(type: "integer", nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    weight_g = table.Column<int>(type: "integer", nullable: false),
                    largest_size_class = table.Column<string>(
                        type: "character varying(2)",
                        maxLength: 2,
                        nullable: false
                    ),
                    required_vehicle = table.Column<string>(
                        type: "character varying(10)",
                        maxLength: 10,
                        nullable: false
                    ),
                    age_check_required = table.Column<bool>(type: "boolean", nullable: false),
                    age_checked = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    courier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_reason = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    assigned_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    arrived_at_pickup_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    picked_up_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    arrived_at_customer_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    delivered_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    failed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipments_couriers_courier_id",
                        column: x => x.courier_id,
                        principalSchema: "carriers",
                        principalTable: "couriers",
                        principalColumn: "id"
                    );
                    table.ForeignKey(
                        name: "fk_shipments_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "ordering",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "fk_shipments_pickup_points_pickup_point_id",
                        column: x => x.pickup_point_id,
                        principalSchema: "crm",
                        principalTable: "pickup_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "fk_shipments_vehicle_types_required_vehicle",
                        column: x => x.required_vehicle,
                        principalSchema: "carriers",
                        principalTable: "vehicle_types",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "pings",
                schema: "tracking",
                columns: table => new
                {
                    id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityAlwaysColumn
                        ),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leg = table.Column<string>(
                        type: "character varying(12)",
                        maxLength: 12,
                        nullable: false
                    ),
                    latitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: false
                    ),
                    longitude = table.Column<decimal>(
                        type: "numeric(9,6)",
                        precision: 9,
                        scale: 6,
                        nullable: false
                    ),
                    recorded_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pings", x => x.id);
                    table.ForeignKey(
                        name: "fk_pings_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "dispatch",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "routes",
                schema: "tracking",
                columns: table => new
                {
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile = table.Column<string>(
                        type: "character varying(10)",
                        maxLength: 10,
                        nullable: false
                    ),
                    approximate = table.Column<bool>(type: "boolean", nullable: false),
                    to_pickup = table.Column<string>(type: "jsonb", nullable: false),
                    to_pickup_distance_m = table.Column<int>(type: "integer", nullable: false),
                    to_pickup_duration_s = table.Column<int>(type: "integer", nullable: false),
                    to_customer = table.Column<string>(type: "jsonb", nullable: false),
                    to_customer_distance_m = table.Column<int>(type: "integer", nullable: false),
                    to_customer_duration_s = table.Column<int>(type: "integer", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    promised_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    simulated = table.Column<bool>(type: "boolean", nullable: false),
                    sim_leg = table.Column<string>(
                        type: "character varying(12)",
                        maxLength: 12,
                        nullable: true
                    ),
                    sim_travelled_m = table.Column<int>(type: "integer", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routes", x => x.shipment_id);
                    table.ForeignKey(
                        name: "fk_routes_couriers_courier_id",
                        column: x => x.courier_id,
                        principalSchema: "carriers",
                        principalTable: "couriers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "fk_routes_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "dispatch",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.InsertData(
                schema: "ordering",
                table: "delivery_fee_bands",
                columns: new[] { "max_distance_m", "fee_minor" },
                values: new object[,]
                {
                    { 2000, 599L },
                    { 5000, 899L },
                    { 10000, 1299L },
                    { 40000, 1999L },
                }
            );

            migrationBuilder.InsertData(
                schema: "carriers",
                table: "vehicle_types",
                columns: new[]
                {
                    "code",
                    "avg_speed_kmh",
                    "max_distance_m",
                    "max_load_g",
                    "max_size_class",
                    "name",
                    "needs_licence",
                    "sort_order",
                },
                values: new object[,]
                {
                    { "bike", 15, 5000, 8000, "M", "Bike", false, 1 },
                    { "car", 25, 25000, 40000, "L", "Car", true, 2 },
                    { "van", 22, 40000, 500000, "XL", "Van", true, 3 },
                }
            );

            migrationBuilder.InsertData(
                schema: "billing",
                table: "payout_rates",
                columns: new[] { "vehicle_type", "base_minor", "per_km_minor" },
                values: new object[,]
                {
                    { "bike", 600L, 120L },
                    { "car", 800L, 160L },
                    { "van", 1200L, 220L },
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_courier_documents_courier_id_kind",
                schema: "compliance",
                table: "courier_documents",
                columns: new[] { "courier_id", "kind" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_couriers_email",
                schema: "carriers",
                table: "couriers",
                column: "email",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_couriers_shift_status",
                schema: "carriers",
                table: "couriers",
                column: "shift_status",
                filter: "shift_status <> 'offline'"
            );

            migrationBuilder.CreateIndex(
                name: "ix_couriers_vehicle_type",
                schema: "carriers",
                table: "couriers",
                column: "vehicle_type"
            );

            migrationBuilder.CreateIndex(
                name: "ix_customer_addresses_customer_id",
                schema: "crm",
                table: "customer_addresses",
                column: "customer_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_customers_email",
                schema: "crm",
                table: "customers",
                column: "email",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_entries_booked_at",
                schema: "billing",
                table: "entries",
                column: "booked_at"
            );

            migrationBuilder.CreateIndex(
                name: "ix_entries_courier_id_booked_at",
                schema: "billing",
                table: "entries",
                columns: new[] { "courier_id", "booked_at" },
                filter: "courier_id IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "ix_entries_event_id",
                schema: "billing",
                table: "entries",
                column: "event_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_entries_order_id",
                schema: "billing",
                table: "entries",
                column: "order_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_entries_order_id_kind",
                schema: "billing",
                table: "entries",
                columns: new[] { "order_id", "kind" },
                unique: true,
                filter: "kind <> 'refund'"
            );

            migrationBuilder.CreateIndex(
                name: "ix_entries_order_id_line_no",
                schema: "billing",
                table: "entries",
                columns: new[] { "order_id", "line_no" },
                unique: true,
                filter: "kind = 'refund' AND line_no IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "ix_entries_order_id1",
                schema: "billing",
                table: "entries",
                column: "order_id",
                unique: true,
                filter: "kind = 'refund' AND line_no IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "ix_entries_partner_id_booked_at",
                schema: "billing",
                table: "entries",
                columns: new[] { "partner_id", "booked_at" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_order_id_product_id",
                schema: "ordering",
                table: "order_lines",
                columns: new[] { "order_id", "product_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_product_id",
                schema: "ordering",
                table: "order_lines",
                column: "product_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_orders_code",
                schema: "ordering",
                table: "orders",
                column: "code",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_id",
                schema: "ordering",
                table: "orders",
                column: "customer_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_orders_partner_id",
                schema: "ordering",
                table: "orders",
                column: "partner_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_orders_pickup_point_id",
                schema: "ordering",
                table: "orders",
                column: "pickup_point_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_orders_placed_at_id",
                schema: "ordering",
                table: "orders",
                columns: new[] { "placed_at", "id" },
                descending: new[] { true, false }
            );

            migrationBuilder.CreateIndex(
                name: "ix_orders_placed_at_id1",
                schema: "ordering",
                table: "orders",
                columns: new[] { "placed_at", "id" },
                filter: "status IN ('placed', 'picking')"
            );

            migrationBuilder.CreateIndex(
                name: "ix_orders_status_placed_at",
                schema: "ordering",
                table: "orders",
                columns: new[] { "status", "placed_at" },
                descending: new[] { false, true }
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at_id",
                schema: "messaging",
                table: "outbox_messages",
                columns: new[] { "occurred_at", "id" },
                filter: "processed_at IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at",
                schema: "messaging",
                table: "outbox_messages",
                column: "processed_at",
                filter: "processed_at IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "ix_partners_code",
                schema: "crm",
                table: "partners",
                column: "code",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_pickup_points_partner_id",
                schema: "crm",
                table: "pickup_points",
                column: "partner_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_pings_shipment_id_id",
                schema: "tracking",
                table: "pings",
                columns: new[] { "shipment_id", "id" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_products_partner_id_category",
                schema: "catalog",
                table: "products",
                columns: new[] { "partner_id", "category" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_products_partner_id_sku",
                schema: "catalog",
                table: "products",
                columns: new[] { "partner_id", "sku" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_routes_courier_id",
                schema: "tracking",
                table: "routes",
                column: "courier_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_routes_shipment_id",
                schema: "tracking",
                table: "routes",
                column: "shipment_id",
                filter: "simulated"
            );

            migrationBuilder.CreateIndex(
                name: "ix_shipments_code",
                schema: "dispatch",
                table: "shipments",
                column: "code",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_shipments_courier_id",
                schema: "dispatch",
                table: "shipments",
                column: "courier_id",
                filter: "courier_id IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "ix_shipments_created_at_id",
                schema: "dispatch",
                table: "shipments",
                columns: new[] { "created_at", "id" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_shipments_order_id",
                schema: "dispatch",
                table: "shipments",
                column: "order_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_shipments_pickup_point_id",
                schema: "dispatch",
                table: "shipments",
                column: "pickup_point_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_shipments_required_vehicle",
                schema: "dispatch",
                table: "shipments",
                column: "required_vehicle"
            );

            migrationBuilder.CreateIndex(
                name: "ix_shipments_status_created_at",
                schema: "dispatch",
                table: "shipments",
                columns: new[] { "status", "created_at" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "identity",
                table: "users",
                column: "email",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                schema: "identity",
                table: "users",
                column: "username",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "courier_documents", schema: "compliance");

            migrationBuilder.DropTable(name: "customer_addresses", schema: "crm");

            migrationBuilder.DropTable(name: "delivery_fee_bands", schema: "ordering");

            migrationBuilder.DropTable(name: "entries", schema: "billing");

            migrationBuilder.DropTable(name: "order_lines", schema: "ordering");

            migrationBuilder.DropTable(name: "outbox_messages", schema: "messaging");

            migrationBuilder.DropTable(name: "payout_rates", schema: "billing");

            migrationBuilder.DropTable(name: "pings", schema: "tracking");

            migrationBuilder.DropTable(name: "routes", schema: "tracking");

            migrationBuilder.DropTable(name: "users", schema: "identity");

            migrationBuilder.DropTable(name: "processed_events", schema: "billing");

            migrationBuilder.DropTable(name: "products", schema: "catalog");

            migrationBuilder.DropTable(name: "shipments", schema: "dispatch");

            migrationBuilder.DropTable(name: "couriers", schema: "carriers");

            migrationBuilder.DropTable(name: "orders", schema: "ordering");

            migrationBuilder.DropTable(name: "vehicle_types", schema: "carriers");

            migrationBuilder.DropTable(name: "customers", schema: "crm");

            migrationBuilder.DropTable(name: "pickup_points", schema: "crm");

            migrationBuilder.DropTable(name: "partners", schema: "crm");

            migrationBuilder.DropSequence(name: "order_code_seq", schema: "ordering");

            migrationBuilder.DropSequence(name: "shipment_code_seq", schema: "dispatch");
        }
    }
}
