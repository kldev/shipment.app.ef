using Microsoft.Extensions.Options;
using Npgsql;
using Shipment.Messaging.Contracts;
using Wolverine;
using Wolverine.RDBMS;
using Wolverine.Runtime;

namespace Shipment.Runner.Outbox;

/// <summary>
/// Hands messaging.outbox_messages on to Wolverine (as Ambit's worker). One transaction per batch: the rows are locked
/// (FOR UPDATE SKIP LOCKED - a second runner takes other rows), each message goes into Wolverine's outbox in the same
/// transaction and the row is marked processed - so a message leaves the outbox once, and Wolverine sends it to RabbitMQ after the commit,
/// retrying from its own storage while the broker is away.
/// </summary>
public sealed class OutboxRelay(
    NpgsqlDataSource dataSource,
    IWolverineRuntime runtime,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxRelay> logger
)
{
    /// <summary>The header naming the outbox row a message came from.</summary>
    public const string OutboxIdHeader = "shipment-outbox-id";

    /// <summary>One batch; the number of rows it looked at (0 = nothing pending).</summary>
    public async Task<int> RelayAsync(CancellationToken ct)
    {
        var settings = options.Value;
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var rows = new List<(Guid Id, string Type, string Payload)>();
        await using (
            var claim = new NpgsqlCommand(
                """
                SELECT id, message_type, payload::text
                FROM messaging.outbox_messages
                WHERE processed_at IS NULL AND attempts < $2
                ORDER BY occurred_at, id
                LIMIT $1
                FOR UPDATE SKIP LOCKED
                """,
                connection,
                transaction
            )
        )
        {
            claim.Parameters.AddWithValue(settings.BatchSize);
            claim.Parameters.AddWithValue(settings.MaxAttempts);
            await using var reader = await claim.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }
        if (rows.Count == 0)
            return 0;

        var context = new MessageContext(runtime);
        await context.EnlistInOutboxAsync(
            new DatabaseEnvelopeTransaction((IMessageDatabase)runtime.Storage, transaction)
        );

        foreach (var (id, type, payload) in rows)
        {
            // A row this version cannot read is marked and skipped; a throw here would roll the whole batch back and
            // the row would block every message after it.
            if (Read(type, payload) is not { } message)
            {
                logger.LogWarning(
                    "Outbox message {OutboxId} of type {MessageType} cannot be read by this version",
                    id,
                    type
                );
                await ExecuteAsync(
                    connection,
                    transaction,
                    "UPDATE messaging.outbox_messages SET attempts = attempts + 1, last_error = left($2, 2000) WHERE id = $1",
                    ct,
                    id,
                    $"Unknown message type {type} or an unreadable payload."
                );
                continue;
            }

            await context.PublishAsync(
                message,
                new DeliveryOptions { Headers = { [OutboxIdHeader] = id.ToString() } }
            );
            await ExecuteAsync(
                connection,
                transaction,
                "UPDATE messaging.outbox_messages SET processed_at = now(), attempts = attempts + 1, last_error = NULL WHERE id = $1",
                ct,
                id
            );
        }

        await transaction.CommitAsync(ct);
        await context.FlushOutgoingMessagesAsync();
        return rows.Count;
    }

    private static object? Read(string type, string payload)
    {
        try
        {
            return OutboxMessageTypes.Deserialize(type, payload);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken ct,
        params object[] values
    )
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values)
            command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Processed rows past the retention; the number deleted.</summary>
    public async Task<int> DeleteProcessedAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            "DELETE FROM messaging.outbox_messages WHERE processed_at < $1"
        );
        command.Parameters.AddWithValue(timeProvider.GetUtcNow() - options.Value.Retention);
        return await command.ExecuteNonQueryAsync(ct);
    }
}
