namespace Shipment.Domain.Messaging;

/// <summary>
/// The transactional outbox (ADR 0007): a change that wants a reaction elsewhere adds a message in the same SaveChanges;
/// the runner hands it on to RabbitMQ. The payload carries ids, never secrets.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public OutboxMessage(Guid id, string messageType, string payload, DateTimeOffset occurredAt)
    {
        Id = id;
        MessageType = messageType;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    /// <summary>kebab-case name of the message (Shipment.Messaging.Contracts.OutboxMessageTypes).</summary>
    public string MessageType { get; private set; } = "";

    /// <summary>JSON (jsonb), web defaults.</summary>
    public string Payload { get; private set; } = "";

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }
}
