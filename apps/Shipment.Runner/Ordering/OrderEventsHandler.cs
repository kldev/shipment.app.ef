using JetBrains.Annotations;
using Shipment.Messaging.Contracts;

namespace Shipment.Runner.Ordering;

/// <summary>
/// The order events, logged as the runner takes them. Wolverine combines every handler of a message type into one
/// (dispatch's <c>ShipmentCreateHandler</c> is the other one of order-picked); billing and notifications add theirs
/// with specs 006 and 007. Until someone reacts, the log line acknowledges the event, so it never waits in the dead
/// letters.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)] // found by Wolverine's handler discovery
public static class OrderEventsHandler
{
    public static void Handle(OrderPlaced message, ILogger<OrderPlaced> logger) =>
        logger.LogInformation("Order {Code} ({OrderId}) placed", message.Code, message.OrderId);

    public static void Handle(OrderPicked message, ILogger<OrderPicked> logger) =>
        logger.LogInformation(
            "Order {Code} ({OrderId}) packed: {Packed} lines, {Missing} missing",
            message.Code,
            message.OrderId,
            message.PickedLines.Count,
            message.MissingLines.Count
        );

    public static void Handle(OrderCancelled message, ILogger<OrderCancelled> logger) =>
        logger.LogInformation(
            "Order {OrderId} cancelled: {Reason}",
            message.OrderId,
            message.Reason
        );
}
