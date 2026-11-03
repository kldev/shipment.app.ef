using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shipment.Persistence.Migrations
{
    /// <summary>
    /// The processes' roles (ADR 0013): shipment_api (the host) and shipment_runner (the runner) get DML on the modules'
    /// tables and nothing else; the migrator owns the schema. NOLOGIN here - Shipment.DbMigrator finish gives them
    /// LOGIN and a password, so no credential is in the repository. Roles are server-wide on the shared server, hence
    /// the prefix and IF NOT EXISTS. The ledger stays append-only even for the owner (a trigger), and the runner owns
    /// the schema of Wolverine's message storage, which Wolverine creates at start.
    /// </summary>
    public partial class AccessRoles : Migration
    {
        private const string Schemas =
            "identity, crm, catalog, carriers, compliance, ordering, dispatch, tracking, billing, messaging";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'shipment_api') THEN
                        CREATE ROLE shipment_api NOLOGIN;
                    END IF;
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'shipment_runner') THEN
                        CREATE ROLE shipment_runner NOLOGIN;
                    END IF;
                END
                $$;
                """
            );
            migrationBuilder.Sql("REVOKE ALL ON SCHEMA public FROM PUBLIC;");
            migrationBuilder.Sql(
                $"GRANT USAGE ON SCHEMA {Schemas} TO shipment_api, shipment_runner;"
            );
            migrationBuilder.Sql(
                $"""
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {Schemas} TO shipment_api, shipment_runner;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {Schemas} TO shipment_api, shipment_runner;
                ALTER DEFAULT PRIVILEGES IN SCHEMA {Schemas}
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO shipment_api, shipment_runner;
                ALTER DEFAULT PRIVILEGES IN SCHEMA {Schemas}
                    GRANT USAGE, SELECT ON SEQUENCES TO shipment_api, shipment_runner;
                """
            );
            // Reference data is the migrations': the processes only read it.
            migrationBuilder.Sql(
                """
                REVOKE INSERT, UPDATE, DELETE ON carriers.vehicle_types, ordering.delivery_fee_bands,
                    billing.payout_rates FROM shipment_api, shipment_runner;
                REVOKE UPDATE, DELETE ON billing.entries FROM shipment_api, shipment_runner;
                """
            );
            migrationBuilder.Sql(
                """
                CREATE FUNCTION billing.entries_append_only()
                    RETURNS trigger
                    LANGUAGE plpgsql
                    SET search_path = pg_catalog, pg_temp
                AS $$
                BEGIN
                    RAISE EXCEPTION 'The ledger is append-only: book a new entry instead.';
                END
                $$;
                CREATE TRIGGER trg_entries_append_only BEFORE UPDATE OR DELETE ON billing.entries
                    FOR EACH ROW EXECUTE FUNCTION billing.entries_append_only();
                """
            );
            // CREATE SCHEMA ... AUTHORIZATION needs SET on the role (PostgreSQL 16+); the migrator created it. A new schema
            // gives PUBLIC nothing, so only its owner (the runner) can use it.
            migrationBuilder.Sql(
                """
                GRANT shipment_runner TO CURRENT_USER WITH SET TRUE, INHERIT FALSE;
                CREATE SCHEMA IF NOT EXISTS wolverine AUTHORIZATION shipment_runner;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS wolverine CASCADE;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_entries_append_only ON billing.entries;"
            );
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.entries_append_only();");
            migrationBuilder.Sql(
                $"""
                ALTER DEFAULT PRIVILEGES IN SCHEMA {Schemas}
                    REVOKE ALL ON TABLES FROM shipment_api, shipment_runner;
                ALTER DEFAULT PRIVILEGES IN SCHEMA {Schemas}
                    REVOKE ALL ON SEQUENCES FROM shipment_api, shipment_runner;
                REVOKE ALL ON ALL TABLES IN SCHEMA {Schemas} FROM shipment_api, shipment_runner;
                REVOKE ALL ON ALL SEQUENCES IN SCHEMA {Schemas} FROM shipment_api, shipment_runner;
                REVOKE USAGE ON SCHEMA {Schemas} FROM shipment_api, shipment_runner;
                """
            );
        }
    }
}
