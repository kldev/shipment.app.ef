using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shipment.Persistence;

namespace Shipment.DbMigrator;

/// <summary>What the bootstrap sets up: names come from configuration, passwords are secrets (environment).</summary>
/// <param name="Logins">Role name to password: LOGIN for the roles the migration AccessRoles creates NOLOGIN.</param>
/// <param name="Password">
/// The accounts' password (ADR 0011): never in the repository - from .env locally, random in the tests. Null: no
/// account can sign in.
/// </param>
public sealed record BootstrapSettings(
    string Database,
    string Migrator,
    string MigratorPassword,
    IReadOnlyDictionary<string, string> Logins,
    string? Password = null
);

/// <summary>
/// The deployment of the database: the superuser's part around the EF Core migrations, the migrations themselves and the
/// demo data. Idempotent: run again, it applies only new migrations and resets the passwords.
/// Names go through format(%I / %L) in PostgreSQL itself - nothing is concatenated into SQL here.
/// </summary>
public static class DatabaseBootstrap
{
    /// <summary>Before the migrations: the migrator (LOGIN, CREATEROLE, never a superuser) and its database.</summary>
    public static async Task PrepareAsync(
        string superuserConnection,
        BootstrapSettings settings,
        CancellationToken ct
    )
    {
        await using var connection = new NpgsqlConnection(superuserConnection);
        await connection.OpenAsync(ct);
        await ExecuteFormattedAsync(
            connection,
            """
            SELECT format('CREATE ROLE %I LOGIN CREATEROLE PASSWORD %L', $1, $2)
            WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = $1)
            """,
            ct,
            settings.Migrator,
            settings.MigratorPassword
        );
        await ExecuteFormattedAsync(
            connection,
            "SELECT format('ALTER ROLE %I LOGIN NOSUPERUSER CREATEROLE PASSWORD %L', $1, $2)",
            ct,
            settings.Migrator,
            settings.MigratorPassword
        );
        await ExecuteFormattedAsync(
            connection,
            """
            SELECT format('CREATE DATABASE %I OWNER %I', $1, $2)
            WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = $1)
            """,
            ct,
            settings.Database,
            settings.Migrator
        );
    }

    /// <summary>
    /// The EF Core migrations as the migrator (the owner of the database and of every table), then - on an empty
    /// database - the demo data in one transaction.
    /// </summary>
    public static async Task MigrateAsync(
        string migratorConnection,
        bool seed,
        CancellationToken ct
    )
    {
        await using var dataSource = NpgsqlDataSource.Create(migratorConnection);
        var options = new DbContextOptionsBuilder<ShipmentDbContext>();
        options.UseShipmentNpgsql(dataSource);
        await using var db = new ShipmentDbContext(options.Options);
        await db.Database.MigrateAsync(ct);
        if (seed && !await db.Users.AnyAsync(ct))
            await SeedAsync(dataSource, ct);
    }

    /// <summary>The Kraków demo (Seed/*.sql, in name order) - only into a database without people yet.</summary>
    private static async Task SeedAsync(NpgsqlDataSource dataSource, CancellationToken ct)
    {
        var assembly = typeof(DatabaseBootstrap).Assembly;
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        foreach (
            var name in assembly
                .GetManifestResourceNames()
                .Where(n => n.StartsWith("Seed/"))
                .Order(StringComparer.Ordinal)
        )
        {
            await using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            await using var command = new NpgsqlCommand(
                await reader.ReadToEndAsync(ct),
                connection,
                transaction
            );
            await command.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// After the migrations: LOGIN and a password for each application role (they must exist - the migration
    /// AccessRoles creates them), and the accounts' password.
    /// </summary>
    public static async Task FinishAsync(
        string superuserConnection,
        BootstrapSettings settings,
        CancellationToken ct
    )
    {
        var target = new NpgsqlConnectionStringBuilder(superuserConnection)
        {
            Database = settings.Database,
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(target);
        await connection.OpenAsync(ct);
        foreach (var (role, password) in settings.Logins)
        {
            if (!await RoleExistsAsync(connection, role, ct))
                throw new InvalidOperationException(
                    $"Role {role} does not exist - run the migrations (migrate) before the logins."
                );
            await ExecuteFormattedAsync(
                connection,
                "SELECT format('ALTER ROLE %I LOGIN PASSWORD %L', $1, $2)",
                ct,
                role,
                password
            );
        }
        await SetPasswordAsync(connection, settings.Password, ct);
    }

    /// <summary>
    /// Every account gets the one shared password (a local learning project - ADR 0011), hashed here; without one
    /// nobody can sign in. No password is in the repository.
    /// </summary>
    private static async Task SetPasswordAsync(
        NpgsqlConnection connection,
        string? password,
        CancellationToken ct
    )
    {
        await using var command = new NpgsqlCommand(
            "UPDATE identity.users SET password_hash = $1",
            connection
        );
        command.Parameters.AddWithValue(
            string.IsNullOrEmpty(password)
                ? DBNull.Value
                : new PasswordHasher<object>().HashPassword(new object(), password)
        );
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> RoleExistsAsync(
        NpgsqlConnection connection,
        string role,
        CancellationToken ct
    )
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = $1)",
            connection
        );
        command.Parameters.AddWithValue(role);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>Lets PostgreSQL build the statement (format with %I and %L), then runs it - when there is one.</summary>
    private static async Task ExecuteFormattedAsync(
        NpgsqlConnection connection,
        string formatQuery,
        CancellationToken ct,
        params object[] values
    )
    {
        string? statement;
        await using (var format = new NpgsqlCommand(formatQuery, connection))
        {
            foreach (var value in values)
                format.Parameters.AddWithValue(value);
            statement = await format.ExecuteScalarAsync(ct) as string;
        }
        if (statement is null)
            return;
        await using var command = new NpgsqlCommand(statement, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
