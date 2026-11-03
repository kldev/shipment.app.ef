using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shipment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "crm",
                table: "pickup_points",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "active",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20
            );

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "crm",
                table: "partners",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "active",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20
            );

            migrationBuilder.AlterColumn<string>(
                name: "shift_status",
                schema: "carriers",
                table: "couriers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "offline",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "crm",
                table: "pickup_points",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "active"
            );

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "crm",
                table: "partners",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "active"
            );

            migrationBuilder.AlterColumn<string>(
                name: "shift_status",
                schema: "carriers",
                table: "couriers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "offline"
            );
        }
    }
}
