using Microsoft.Extensions.Configuration;
using Npgsql;
using Shipment.DbMigrator;

// dotnet Shipment.DbMigrator.dll prepare | migrate | finish
//   prepare  the migrator's role and the database (superuser)
//   migrate  the EF Core migrations as the migrator; --seed: the Kraków demo into an empty database
//   finish   the application roles' logins and the accounts' password (superuser)
// Configuration (environment variables; secrets only there):
//   ConnectionStrings__postgres   a superuser on the server (database postgres) - required, no default on purpose
//   Database__Name                shipment
//   Database__Migrator            shipment_migrator
//   Database__MigratorPassword    the migrator's password
//   Logins__<role>                password of each application role, e.g. Logins__shipment_api
//   Accounts__Password            every account's password (finish; ADR 0011) - absent: nobody can sign in
// Exit code 0 when done, 1 on a failure, 2 on wrong usage.
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddCommandLine(args.Skip(1).Where(a => a != "--seed").ToArray())
    .Build();

var command = args.FirstOrDefault();
var superuser = configuration.GetConnectionString("postgres");
var migratorPassword = configuration["Database:MigratorPassword"];
if (command is not ("prepare" or "migrate" or "finish") || string.IsNullOrWhiteSpace(superuser))
{
    Console.Error.WriteLine(
        "Usage: Shipment.DbMigrator prepare|migrate [--seed]|finish  (ConnectionStrings__postgres = a superuser connection)"
    );
    return 2;
}
if (command is "prepare" or "migrate" && string.IsNullOrWhiteSpace(migratorPassword))
{
    Console.Error.WriteLine($"Database__MigratorPassword is required for {command}.");
    return 2;
}

var settings = new BootstrapSettings(
    configuration["Database:Name"] ?? "shipment",
    configuration["Database:Migrator"] ?? "shipment_migrator",
    migratorPassword ?? "",
    configuration
        .GetSection("Logins")
        .GetChildren()
        .Where(login => !string.IsNullOrEmpty(login.Value))
        .ToDictionary(login => login.Key, login => login.Value!),
    Password: configuration["Accounts:Password"]
);

try
{
    switch (command)
    {
        case "prepare":
            await DatabaseBootstrap.PrepareAsync(superuser, settings, CancellationToken.None);
            Console.WriteLine($"Prepared: role {settings.Migrator}, database {settings.Database}.");
            break;
        case "migrate":
            // The migrator on the same server as the superuser, in its own database.
            var migrator = new NpgsqlConnectionStringBuilder(superuser)
            {
                Database = settings.Database,
                Username = settings.Migrator,
                Password = settings.MigratorPassword,
            }.ConnectionString;
            var seed = args.Contains("--seed");
            await DatabaseBootstrap.MigrateAsync(migrator, seed, CancellationToken.None);
            Console.WriteLine(
                $"Migrated: database {settings.Database}{(seed ? ", demo data in place" : "")}."
            );
            break;
        default:
            await DatabaseBootstrap.FinishAsync(superuser, settings, CancellationToken.None);
            Console.WriteLine(
                $"Finished: LOGIN for {string.Join(", ", settings.Logins.Keys)}; the accounts' password."
            );
            break;
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"{command} failed: {exception.Message}");
    return 1;
}
