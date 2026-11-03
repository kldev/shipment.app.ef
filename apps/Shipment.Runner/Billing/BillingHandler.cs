using JetBrains.Annotations;
using Shipment.Application.Billing;
using Shipment.Application.Common;
using Shipment.Domain.Dispatch;
using Shipment.Messaging.Contracts;
using Shipment.Runner.Messaging;
using Shipment.Runner.Outbox;
using Wolverine;

namespace Shipment.Runner.Billing;

/// <summary>
/// Billing's reactions (ADR 0007), each with the event's id - the outbox row's id the relay sent along (billing keeps
/// it, so the same event never books twice): order-placed authorizes the payment, order-picked captures the packed
/// goods and the fee and refunds the missing lines, order-cancelled and a failed delivery refund in full,
/// shipment-delivered books the commission and the courier's payout.
/// </summary>
// Found by Wolverine: a public class named *Handler with Handle methods in the runner's assembly.
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class BillingHandler(BillingService billing)
{
    public async Task Handle(OrderPlaced message, Envelope envelope, CancellationToken ct) =>
        (await billing.AuthorizeAsync(EventId(envelope), message.OrderId, ct)).ThrowIfFailed(
            "Authorize"
        );

    public async Task Handle(OrderPicked message, Envelope envelope, CancellationToken ct) =>
        (await billing.SettleAsync(EventId(envelope), message.OrderId, ct)).ThrowIfFailed("Settle");

    public async Task Handle(OrderCancelled message, Envelope envelope, CancellationToken ct) =>
        (
            await billing.RefundAsync(
                EventId(envelope),
                "order-cancelled",
                message.OrderId,
                message.Reason,
                ct
            )
        ).ThrowIfFailed("Refund");

    public async Task Handle(ShipmentStatusChanged message, Envelope envelope, CancellationToken ct)
    {
        if (message.Status != Wire.Name(ShipmentStatus.Failed))
            return;
        (
            await billing.RefundAsync(
                EventId(envelope),
                "shipment-failed",
                message.OrderId,
                message.Reason ?? "Delivery failed.",
                ct
            )
        ).ThrowIfFailed("Refund");
    }

    public async Task Handle(ShipmentDelivered message, Envelope envelope, CancellationToken ct) =>
        (
            await billing.SettleDeliveryAsync(EventId(envelope), message.ShipmentId, ct)
        ).ThrowIfFailed("Settle delivery");

    /// <summary>The outbox row's id; a message published without it (none in Shipment) keeps Wolverine's own.</summary>
    private static Guid EventId(Envelope envelope) =>
        envelope.Headers.TryGetValue(OutboxRelay.OutboxIdHeader, out var id)
        && Guid.TryParse(id, out var outboxId)
            ? outboxId
            : envelope.Id;
}
