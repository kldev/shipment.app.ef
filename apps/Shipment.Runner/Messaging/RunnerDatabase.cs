using Npgsql;
using Shipment.Domain.Common;

namespace Shipment.Runner.Messaging;

/// <summary>The runner's database login (shipment_runner): the modules' tables through EF, the outbox and Wolverine's schema.</summary>
public static class RunnerDatabase
{
    public const string ConnectionStringName = "shipment";

    /// <summary>
    /// The connection string for Wolverine's own pool too: no Kerberos on the shared server - without
    /// GssEncryptionMode.Disable Npgsql probes GSS encryption first and logs a missing libgssapi_krb5 in the image.
    /// </summary>
    public static string Normalize(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "shipment-runner",
            GssEncryptionMode = GssEncryptionMode.Disable,
        }.ConnectionString;
}

/// <summary>The runner acts for the system when it reacts to an integration event: no user (ADR 0007).</summary>
internal sealed class SystemActor : ICurrentActor
{
    public Guid? UserId => null;
}

internal static class Reactions
{
    /// <summary>A refusal in a reaction is a failure: Wolverine retries the message, then dead-letters it for a person.</summary>
    public static void ThrowIfFailed(this Result result, string what)
    {
        if (result.Error is { } error)
            throw new InvalidOperationException($"{what}: {error.Message}");
    }
}
