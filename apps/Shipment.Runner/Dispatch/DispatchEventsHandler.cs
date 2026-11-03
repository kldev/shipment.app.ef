using JetBrains.Annotations;
using Shipment.Messaging.Contracts;

namespace Shipment.Runner.Dispatch;

/// <summary>
/// The dispatch events, logged as the runner takes them (see <c>OrderEventsHandler</c>). Ordering reacts to the status
/// changes (<c>OrderCompleteHandler</c>); tracking, billing and notifications add theirs with specs 005-007.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)] // found by Wolverine's handler discovery
public static class DispatchEventsHandler
{
    public static void Handle(CourierAssigned message, ILogger<CourierAssigned> logger) =>
        logger.LogInformation(
            "Shipment {Code} ({ShipmentId}) of order {OrderId} assigned to courier {CourierId}",
            message.Code,
            message.ShipmentId,
            message.OrderId,
            message.CourierId
        );

    public static void Handle(
        ShipmentStatusChanged message,
        ILogger<ShipmentStatusChanged> logger
    ) =>
        logger.LogInformation(
            "Shipment {Code} ({ShipmentId}) is {Status}",
            message.Code,
            message.ShipmentId,
            message.Status
        );

    public static void Handle(ShipmentDelivered message, ILogger<ShipmentDelivered> logger) =>
        logger.LogInformation(
            "Shipment {Code} ({ShipmentId}) of order {OrderId} delivered by courier {CourierId}",
            message.Code,
            message.ShipmentId,
            message.OrderId,
            message.CourierId
        );
}
