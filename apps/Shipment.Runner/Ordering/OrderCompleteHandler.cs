using JetBrains.Annotations;
using Shipment.Application.Common;
using Shipment.Application.Ordering;
using Shipment.Domain.Dispatch;
using Shipment.Messaging.Contracts;
using Shipment.Runner.Messaging;

namespace Shipment.Runner.Ordering;

/// <summary>
/// Ordering's reaction to shipment-status-changed (ADR 0007): a delivered or failed shipment ends the order (the other
/// steps and a repeated event change nothing).
/// </summary>
// Found by Wolverine (a public class with Handle in the runner's assembly).
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class OrderCompleteHandler(OrderingService ordering)
{
    public async Task Handle(ShipmentStatusChanged message, CancellationToken ct)
    {
        var delivered = message.Status == Wire.Name(ShipmentStatus.Delivered);
        if (!delivered && message.Status != Wire.Name(ShipmentStatus.Failed))
            return;
        (
            await ordering.CompleteAsync(message.OrderId, delivered, message.Reason, ct)
        ).ThrowIfFailed("Complete order");
    }
}
