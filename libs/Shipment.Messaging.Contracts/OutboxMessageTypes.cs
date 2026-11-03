using System.Text.Json;

namespace Shipment.Messaging.Contracts;

/// <summary>
/// The messages of messaging.outbox_messages and the name each one is stored under (message_type) - rows already in
/// the outbox carry these names, so a rename keeps the old one too. The payload is JSON in the web defaults (camelCase).
/// </summary>
public static class OutboxMessageTypes
{
    // The integration events between modules (docs/domain.md, "Integration events").
    private static readonly Dictionary<string, Type> ByName = new(StringComparer.Ordinal)
    {
        ["order-placed"] = typeof(OrderPlaced),
        ["order-picked"] = typeof(OrderPicked),
        ["order-cancelled"] = typeof(OrderCancelled),
        ["courier-assigned"] = typeof(CourierAssigned),
        ["shipment-status-changed"] = typeof(ShipmentStatusChanged),
        ["shipment-delivered"] = typeof(ShipmentDelivered),
    };

    private static readonly Dictionary<Type, string> ByType = ByName.ToDictionary(
        pair => pair.Value,
        pair => pair.Key
    );

    /// <summary>The name a message is stored under; a type that is no outbox message is a bug.</summary>
    public static string NameOf(object message) =>
        ByType.TryGetValue(message.GetType(), out var name)
            ? name
            : throw new ArgumentException(
                $"{message.GetType().Name} is not an outbox message.",
                nameof(message)
            );

    public static string Serialize(object message) =>
        JsonSerializer.Serialize(message, message.GetType(), JsonSerializerOptions.Web);

    /// <summary>The message stored under <paramref name="name"/>, or null for a name this version does not know.</summary>
    public static object? Deserialize(string name, string payload) =>
        ByName.GetValueOrDefault(name) is { } type
            ? JsonSerializer.Deserialize(payload, type, JsonSerializerOptions.Web)
            : null;
}
