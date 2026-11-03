using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Shipment.Persistence;

public static class PersistenceSetup
{
    /// <summary>The migrations' own table, outside the modules' schemas.</summary>
    private const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>
    /// One data source per process and a scoped DbContext factory: Blazor circuits live long, so every operation makes
    /// its own short context. No Kerberos on the shared server - without GssEncryptionMode.Disable Npgsql probes GSS
    /// first and logs a missing libgssapi_krb5 in the aspnet image.
    /// </summary>
    public static IServiceCollection AddShipmentDatabase(
        this IServiceCollection services,
        string connectionString,
        string applicationName
    )
    {
        var dataSource = new NpgsqlDataSourceBuilder(connectionString)
        {
            ConnectionStringBuilder =
            {
                ApplicationName = applicationName,
                GssEncryptionMode = GssEncryptionMode.Disable,
            },
        }.Build();
        services.AddSingleton(dataSource);
        services.AddScoped<AuditInterceptor>();
        services.AddDbContextFactory<ShipmentDbContext>(
            (sp, options) =>
                options
                    .UseShipmentNpgsql(dataSource)
                    .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()),
            ServiceLifetime.Scoped
        );
        return services;
    }

    public static DbContextOptionsBuilder UseShipmentNpgsql(
        this DbContextOptionsBuilder options,
        NpgsqlDataSource dataSource
    ) =>
        options
            .UseNpgsql(
                dataSource,
                npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, "public")
            )
            .UseSnakeCaseNamingConvention();
}
