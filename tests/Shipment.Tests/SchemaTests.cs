namespace Shipment.Tests;

/// <summary>
/// The schema the EF Core migrations leave behind (ADR 0013): the processes' roles and what they may do, the ledger's
/// trigger, Wolverine's schema, and an index under every foreign key.
/// </summary>
[Trait("verify", "schema")]
[Collection(ApiCollection.Name)]
public sealed class SchemaTests(ApiFixture api)
{
    [Fact]
    public async Task The_processes_have_their_own_roles_with_dml_only_where_they_need_it()
    {
        Assert.Equal(
            2L,
            await api.ScalarAsync<long>(
                "SELECT count(*) FROM pg_roles WHERE rolname IN ('shipment_api', 'shipment_runner') AND NOT rolsuper"
            )
        );
        Assert.True(
            await api.ScalarAsync<bool>(
                "SELECT has_table_privilege('shipment_api', 'crm.partners', 'SELECT')"
            )
        );
        Assert.True(
            await api.ScalarAsync<bool>(
                "SELECT has_table_privilege('shipment_api', 'crm.partners', 'INSERT')"
            )
        );
        // The ledger takes new entries only; reference data is the migrations'.
        Assert.True(
            await api.ScalarAsync<bool>(
                "SELECT has_table_privilege('shipment_api', 'billing.entries', 'INSERT')"
            )
        );
        Assert.False(
            await api.ScalarAsync<bool>(
                "SELECT has_table_privilege('shipment_api', 'billing.entries', 'UPDATE')"
            )
        );
        Assert.False(
            await api.ScalarAsync<bool>(
                "SELECT has_table_privilege('shipment_runner', 'billing.entries', 'DELETE')"
            )
        );
        Assert.False(
            await api.ScalarAsync<bool>(
                "SELECT has_table_privilege('shipment_api', 'carriers.vehicle_types', 'UPDATE')"
            )
        );
        // And what the host's role sees itself.
        Assert.Equal(
            "shipment_api",
            await api.ScalarAsAsync<string>("shipment_api", "SELECT current_user::text")
        );
    }

    [Fact]
    public async Task The_ledger_has_its_append_only_trigger()
    {
        // The trigger firing on a real entry is BillingTests.The_ledger_is_append_only; here: it is there and enabled
        // before UPDATE and DELETE.
        Assert.Equal(
            "O",
            await api.ScalarAsync<string>(
                """
                SELECT tgenabled::text FROM pg_trigger
                WHERE tgrelid = 'billing.entries'::regclass AND tgname = 'trg_entries_append_only'
                  AND tgtype & 2 = 2 AND tgtype & 8 = 8 AND tgtype & 16 = 16
                """
            )
        );
    }

    [Fact]
    public async Task Wolverine_message_storage_belongs_to_the_runner()
    {
        Assert.Equal(
            "shipment_runner",
            await api.ScalarAsync<string>(
                "SELECT nspowner::regrole::text FROM pg_namespace WHERE nspname = 'wolverine'"
            )
        );
        Assert.False(
            await api.ScalarAsync<bool>(
                "SELECT has_schema_privilege('shipment_api', 'wolverine', 'USAGE')"
            )
        );
    }

    [Fact]
    public async Task Every_foreign_key_of_the_modules_has_an_index_on_the_child_columns()
    {
        // PostgreSQL does not index the child side of a foreign key; without it deleting or re-keying the parent scans
        // the child table once per row. Exempt: the vehicle types (a small reference list, never deleted). A partial
        // index counts only with "<column> IS NOT NULL" (the FK check looks up non-null values).
        var missing = await api.ScalarAsync<string>(
            """
            SELECT coalesce(string_agg(c.conrelid::regclass || '.' || a.attname || ' -> ' || c.confrelid::regclass,
                ', ' ORDER BY c.conrelid::regclass::text, a.attname), '')
            FROM pg_constraint AS c
            JOIN pg_attribute AS a ON a.attrelid = c.conrelid AND a.attnum = c.conkey[1]
            WHERE c.contype = 'f'
              AND c.connamespace::regnamespace::text IN
                  ('identity', 'crm', 'catalog', 'carriers', 'compliance', 'ordering', 'dispatch', 'tracking',
                   'billing', 'messaging')
              AND c.confrelid <> 'carriers.vehicle_types'::regclass
              AND NOT EXISTS (
                  SELECT 1 FROM pg_index AS i
                  WHERE i.indrelid = c.conrelid
                    AND (i.indkey::int2[])[0:cardinality(c.conkey) - 1] @> c.conkey
                    AND (i.indkey::int2[])[0:cardinality(c.conkey) - 1] <@ c.conkey
                    AND (i.indpred IS NULL
                         OR pg_get_expr(i.indpred, i.indrelid) = '(' || a.attname || ' IS NOT NULL)'))
            """
        );

        Assert.Equal("", missing);
    }
}
