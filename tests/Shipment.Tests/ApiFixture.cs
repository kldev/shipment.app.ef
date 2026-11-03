using System.Net;
using System.Net.Http.Headers;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shipment.DbMigrator;
using Testcontainers.PostgreSql;

namespace Shipment.Tests;

/// <summary>
/// The host against a throw-away PostgreSQL 18 deployed the way deploy/deploy.sh does it (Shipment.DbMigrator):
/// prepare as the superuser, the EF Core migrations and the Kraków demo as shipment_migrator (owner of the database,
/// not a superuser), then LOGIN for the processes' roles. The host connects as shipment_api and users sign in for real
/// (the session cookie).
/// </summary>
// Created by xUnit for ICollectionFixture<ApiFixture>.
[UsedImplicitly]
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>The accounts' password for this run: random, like a deployment's Accounts__Password (ADR 0011).</summary>
    public static readonly string Password = $"Test-{Guid.NewGuid():N}";
    public const string Admin = "k.lopuch";
    public static readonly Guid AdminId = Guid.Parse("019a2f00-0000-7000-8000-000000000001");

    private const string ApiPassword = "api-test-only";
    private const string MigratorPassword = "migrator-test-only";
    private const string RunnerPassword = "runner-test-only";

    private PostgreSqlContainer? _postgres;

    // The test host would watch appsettings*.json for changes: an inotify instance per host, and the user's limit (128 by
    // default) is shared with the IDE and every other process - the tests failed with "inotify instances reached".
    static ApiFixture() =>
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");

    /// <summary>The superuser on the database shipment - setup only, never the host.</summary>
    private string AdminConnection =>
        new NpgsqlConnectionStringBuilder(
            _postgres?.GetConnectionString() ?? throw new InvalidOperationException("Not started.")
        )
        {
            Database = "shipment",
        }.ConnectionString;

    private string AppConnection =>
        new NpgsqlConnectionStringBuilder(AdminConnection)
        {
            Username = "shipment_api",
            Password = ApiPassword,
            ApplicationName = "shipment-tests",
        }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:18.6-alpine")
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await _postgres.StartAsync();
        var bootstrap = new BootstrapSettings(
            "shipment",
            "shipment_migrator",
            MigratorPassword,
            new Dictionary<string, string>
            {
                ["shipment_api"] = ApiPassword,
                ["shipment_runner"] = RunnerPassword,
            },
            Password: Password
        );
        await DatabaseBootstrap.PrepareAsync(
            _postgres.GetConnectionString(),
            bootstrap,
            CancellationToken.None
        );
        await DatabaseBootstrap.MigrateAsync(
            new NpgsqlConnectionStringBuilder(AdminConnection)
            {
                Username = "shipment_migrator",
                Password = MigratorPassword,
            }.ConnectionString,
            seed: true,
            CancellationToken.None
        );
        await DatabaseBootstrap.FinishAsync(
            _postgres.GetConnectionString(),
            bootstrap,
            CancellationToken.None
        );
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:shipment", AppConnection);
        // Every test signs in again from the same address; the limit itself is not under test.
        builder.UseSetting("Session:SignInAttemptsPerMinute", "1000");
    }

    /// <summary>A browser-like client signed in through the sign-in form (cookie session), no automatic redirects.</summary>
    public async Task<HttpClient> AsAsync(string username)
    {
        var client = Browser();
        var response = await client.PostAsync(
            "/auth/login",
            new FormUrlEncodedContent(
                new Dictionary<string, string> { ["username"] = username, ["password"] = Password }
            )
        );
        if (
            response.StatusCode != HttpStatusCode.Redirect
            || response.Headers.Location?.OriginalString != "/dashboard"
        )
            throw new InvalidOperationException(
                $"Sign-in of {username} failed: {(int)response.StatusCode} {response.Headers.Location}"
            );
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
        return client;
    }

    /// <summary>A new active admin with the run's password - for tests that change an account.</summary>
    public async Task<string> AddAdminAsync(string username, string displayName)
    {
        await ExecuteAsync(
            $"""
            INSERT INTO identity.users (username, email, password_hash, display_name, role)
            SELECT '{username}', '{username}@test.example', u.password_hash, '{displayName}', 'admin'
            FROM identity.users AS u WHERE u.id = '{AdminId}'
            """
        );
        return username;
    }

    /// <summary>
    /// What the runner does with an integration event (ADR 0007): the reacting module's application service, called in
    /// a scope of the host without a user - the system acts, as in the runner.
    /// </summary>
    public async Task<TResult> RunnerAsync<TService, TResult>(
        Func<TService, Task<TResult>> reaction
    )
        where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        return await reaction(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <summary>Runs SQL as the superuser - test setup only (e.g. making an account inactive).</summary>
    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>One value read as the superuser - for checking what a table stores, not what the API shows.</summary>
    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>One value read as a process's role - what shipment_api or shipment_runner may see and do.</summary>
    public async Task<T> ScalarAsAsync<T>(string role, string sql)
    {
        await using var connection = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(AdminConnection)
            {
                Username = role,
                Password = role == "shipment_runner" ? RunnerPassword : ApiPassword,
            }.ConnectionString
        );
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>A client without a session that keeps cookies and does not follow redirects.</summary>
    public HttpClient Browser() =>
        CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            }
        );

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }
}

/// <summary>One database and one host for every test class: a second PostgreSQL container would only cost memory.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
