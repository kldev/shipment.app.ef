using JetBrains.Annotations;
using Shipment.Application.Dispatch;
using Shipment.Messaging.Contracts;
using Shipment.Runner.Messaging;

namespace Shipment.Runner.Dispatch;

/// <summary>
/// Dispatch's reaction to order-picked (ADR 0007): a shipment for what the store packed - one per order, so a repeated
/// event does nothing. A failure throws: Wolverine tries again, then the dead letters.
/// </summary>
// Found by Wolverine (a public class with Handle in the runner's assembly).
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class ShipmentCreateHandler(DispatchService dispatch)
{
    public async Task Handle(OrderPicked message, CancellationToken ct) =>
        (await dispatch.CreateForOrderAsync(message.OrderId, ct)).ThrowIfFailed("Create shipment");
}
