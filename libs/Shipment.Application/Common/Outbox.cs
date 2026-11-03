using Shipment.Domain.Messaging;
using Shipment.Messaging.Contracts;
using Shipment.Persistence;

namespace Shipment.Application.Common;

internal static class Outbox
{
    /// <summary>An integration event in the same SaveChanges as the change it tells about (ADR 0007).</summary>
    public static void Enqueue(this ShipmentDbContext db, object message, DateTimeOffset now) =>
        db.OutboxMessages.Add(
            new OutboxMessage(
                Guid.CreateVersion7(),
                OutboxMessageTypes.NameOf(message),
                OutboxMessageTypes.Serialize(message),
                now
            )
        );
}
