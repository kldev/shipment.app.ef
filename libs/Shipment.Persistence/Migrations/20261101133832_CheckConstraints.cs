using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shipment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_vehicle_types_size",
                schema: "carriers",
                table: "vehicle_types",
                sql: "max_size_class IN ('S', 'M', 'L', 'XL')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_role",
                schema: "identity",
                table: "users",
                sql: "role IN ('admin')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_status",
                schema: "identity",
                table: "users",
                sql: "status IN ('active', 'inactive')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_username",
                schema: "identity",
                table: "users",
                sql: "username ~ '^[a-z0-9][a-z0-9._-]{1,63}$'"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_shipments_courier",
                schema: "dispatch",
                table: "shipments",
                sql: "(status = 'available') = (courier_id IS NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_shipments_failure",
                schema: "dispatch",
                table: "shipments",
                sql: "(status = 'failed') = (failure_reason IS NOT NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_shipments_freight",
                schema: "dispatch",
                table: "shipments",
                sql: "item_count > 0 AND weight_g >= 0 AND distance_m >= 0"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_shipments_size",
                schema: "dispatch",
                table: "shipments",
                sql: "largest_size_class IN ('S', 'M', 'L', 'XL')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_shipments_status",
                schema: "dispatch",
                table: "shipments",
                sql: "status IN ('available', 'courier_assigned', 'arrived_at_pickup', 'picked_up', 'arrived_at_customer', 'delivered', 'failed')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_routes_legs",
                schema: "tracking",
                table: "routes",
                sql: "jsonb_typeof(to_pickup -> 'coordinates') = 'array' AND jsonb_typeof(to_customer -> 'coordinates') = 'array'"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_routes_profile",
                schema: "tracking",
                table: "routes",
                sql: "profile IN ('bike', 'car')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_routes_sim_leg",
                schema: "tracking",
                table: "routes",
                sql: "sim_leg IS NULL OR sim_leg IN ('to_pickup', 'to_customer')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_category",
                schema: "catalog",
                table: "products",
                sql: "category ~ '^[a-z][a-z_]{1,39}$'"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_price",
                schema: "catalog",
                table: "products",
                sql: "price_minor BETWEEN 1 AND 100000000"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_size_class",
                schema: "catalog",
                table: "products",
                sql: "size_class IN ('S', 'M', 'L', 'XL')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_sku",
                schema: "catalog",
                table: "products",
                sql: "sku ~ '^[A-Z0-9][A-Z0-9-]{1,39}$'"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_weight",
                schema: "catalog",
                table: "products",
                sql: "weight_g BETWEEN 1 AND 500000"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_pings_leg",
                schema: "tracking",
                table: "pings",
                sql: "leg IN ('to_pickup', 'to_customer')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_pings_place",
                schema: "tracking",
                table: "pings",
                sql: "latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_pickup_points_hours",
                schema: "crm",
                table: "pickup_points",
                sql: "opens_minute >= 0 AND closes_minute <= 1440 AND opens_minute < closes_minute"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_pickup_points_place",
                schema: "crm",
                table: "pickup_points",
                sql: "latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_pickup_points_status",
                schema: "crm",
                table: "pickup_points",
                sql: "status IN ('active', 'closed')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_partners_code",
                schema: "crm",
                table: "partners",
                sql: "code ~ '^[A-Z0-9]{2,12}$'"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_partners_commission",
                schema: "crm",
                table: "partners",
                sql: "commission_bps BETWEEN 0 AND 5000"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_partners_kind",
                schema: "crm",
                table: "partners",
                sql: "kind IN ('grocery', 'restaurant', 'pharmacy', 'shop')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_partners_status",
                schema: "crm",
                table: "partners",
                sql: "status IN ('active', 'paused')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_type",
                schema: "messaging",
                table: "outbox_messages",
                sql: "message_type ~ '^[a-z][a-z0-9-]*$'"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_money",
                schema: "ordering",
                table: "orders",
                sql: "delivery_fee_minor >= 0 AND goods_ordered_minor > 0 AND coalesce(goods_picked_minor, 0) >= 0"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_status",
                schema: "ordering",
                table: "orders",
                sql: "status IN ('placed', 'picking', 'ready', 'cancelled', 'delivered', 'failed')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_tip",
                schema: "ordering",
                table: "orders",
                sql: "tip_percent IN (0, 5, 10) AND tip_minor >= 0"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_lines_picked",
                schema: "ordering",
                table: "order_lines",
                sql: "picked_quantity IS NULL OR picked_quantity BETWEEN 0 AND quantity"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_lines_quantity",
                schema: "ordering",
                table: "order_lines",
                sql: "quantity BETWEEN 1 AND 99"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_lines_size",
                schema: "ordering",
                table: "order_lines",
                sql: "size_class IN ('S', 'M', 'L', 'XL')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_entries_amount",
                schema: "billing",
                table: "entries",
                sql: "amount_minor >= 0"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_entries_courier",
                schema: "billing",
                table: "entries",
                sql: "(kind IN ('payout', 'tip')) = (courier_id IS NOT NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_entries_delivery",
                schema: "billing",
                table: "entries",
                sql: "kind NOT IN ('commission', 'payout', 'tip') OR shipment_id IS NOT NULL"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_entries_kind",
                schema: "billing",
                table: "entries",
                sql: "kind IN ('authorization', 'capture_goods', 'capture_fee', 'capture_tip', 'refund', 'commission', 'payout', 'tip')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_entries_line",
                schema: "billing",
                table: "entries",
                sql: "(line_no IS NULL) = (product_id IS NULL) AND (line_no IS NULL) = (quantity IS NULL) AND (line_no IS NULL OR kind = 'refund')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_customers_email",
                schema: "crm",
                table: "customers",
                sql: "email = lower(btrim(email)) AND email LIKE '%_@_%'"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_addresses_place",
                schema: "crm",
                table: "customer_addresses",
                sql: "latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_couriers_plate",
                schema: "carriers",
                table: "couriers",
                sql: "vehicle_type = 'bike' OR vehicle_plate IS NOT NULL"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_couriers_position",
                schema: "carriers",
                table: "couriers",
                sql: "(last_latitude IS NULL) = (last_longitude IS NULL) AND (last_latitude IS NULL) = (last_position_at IS NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_couriers_shift_started",
                schema: "carriers",
                table: "couriers",
                sql: "(shift_status = 'offline') = (shift_started_at IS NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_couriers_shift_status",
                schema: "carriers",
                table: "couriers",
                sql: "shift_status IN ('offline', 'available', 'busy')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_courier_documents_kind",
                schema: "compliance",
                table: "courier_documents",
                sql: "kind IN ('id_card', 'driving_licence', 'vehicle_insurance', 'food_hygiene')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_courier_documents_verified",
                schema: "compliance",
                table: "courier_documents",
                sql: "(verified_at IS NULL) = (verified_by IS NULL)"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_vehicle_types_size",
                schema: "carriers",
                table: "vehicle_types"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_role",
                schema: "identity",
                table: "users"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_status",
                schema: "identity",
                table: "users"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_username",
                schema: "identity",
                table: "users"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_shipments_courier",
                schema: "dispatch",
                table: "shipments"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_shipments_failure",
                schema: "dispatch",
                table: "shipments"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_shipments_freight",
                schema: "dispatch",
                table: "shipments"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_shipments_size",
                schema: "dispatch",
                table: "shipments"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_shipments_status",
                schema: "dispatch",
                table: "shipments"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_routes_legs",
                schema: "tracking",
                table: "routes"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_routes_profile",
                schema: "tracking",
                table: "routes"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_routes_sim_leg",
                schema: "tracking",
                table: "routes"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_category",
                schema: "catalog",
                table: "products"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_price",
                schema: "catalog",
                table: "products"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_size_class",
                schema: "catalog",
                table: "products"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_sku",
                schema: "catalog",
                table: "products"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_weight",
                schema: "catalog",
                table: "products"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_pings_leg",
                schema: "tracking",
                table: "pings"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_pings_place",
                schema: "tracking",
                table: "pings"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_pickup_points_hours",
                schema: "crm",
                table: "pickup_points"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_pickup_points_place",
                schema: "crm",
                table: "pickup_points"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_pickup_points_status",
                schema: "crm",
                table: "pickup_points"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_partners_code",
                schema: "crm",
                table: "partners"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_partners_commission",
                schema: "crm",
                table: "partners"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_partners_kind",
                schema: "crm",
                table: "partners"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_partners_status",
                schema: "crm",
                table: "partners"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_type",
                schema: "messaging",
                table: "outbox_messages"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_money",
                schema: "ordering",
                table: "orders"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_status",
                schema: "ordering",
                table: "orders"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_tip",
                schema: "ordering",
                table: "orders"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_lines_picked",
                schema: "ordering",
                table: "order_lines"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_lines_quantity",
                schema: "ordering",
                table: "order_lines"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_lines_size",
                schema: "ordering",
                table: "order_lines"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_entries_amount",
                schema: "billing",
                table: "entries"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_entries_courier",
                schema: "billing",
                table: "entries"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_entries_delivery",
                schema: "billing",
                table: "entries"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_entries_kind",
                schema: "billing",
                table: "entries"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_entries_line",
                schema: "billing",
                table: "entries"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_customers_email",
                schema: "crm",
                table: "customers"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_addresses_place",
                schema: "crm",
                table: "customer_addresses"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_couriers_plate",
                schema: "carriers",
                table: "couriers"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_couriers_position",
                schema: "carriers",
                table: "couriers"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_couriers_shift_started",
                schema: "carriers",
                table: "couriers"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_couriers_shift_status",
                schema: "carriers",
                table: "couriers"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_courier_documents_kind",
                schema: "compliance",
                table: "courier_documents"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_courier_documents_verified",
                schema: "compliance",
                table: "courier_documents"
            );
        }
    }
}
