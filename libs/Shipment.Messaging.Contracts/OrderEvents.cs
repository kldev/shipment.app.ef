using JetBrains.Annotations;

namespace Shipment.Messaging.Contracts;

/// <summary>
/// An order was placed (OrderingService.PlaceAsync). Ids only: whoever reacts reads what it needs through the module's
/// views. Reacts: notifications (confirmation e-mail, spec 007).
/// </summary>
public sealed record OrderPlaced(Guid OrderId, string Code);

/// <summary>A line of <see cref="OrderPicked"/>: how many of the product were packed, or are missing.</summary>
[UsedImplicitly(
    ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature,
    ImplicitUseTargetFlags.WithMembers
)]
public sealed record PickedLine(int LineNo, Guid ProductId, int Quantity);

/// <summary>
/// The store finished packing and had something (PickingService.FinishAsync). MissingLines is what the catalog listed
/// but the shelf did not have. Reacts: billing (capture the packed goods, refund the missing ones - spec 006), dispatch
/// (a shipment - spec 004), notifications (the partial fulfilment e-mail - spec 007).
/// </summary>
public sealed record OrderPicked(
    Guid OrderId,
    string Code,
    IReadOnlyList<PickedLine> PickedLines,
    IReadOnlyList<PickedLine> MissingLines
);

/// <summary>
/// An order ended before delivery: cancelled by the operator, or nothing of it was on the shelf. Reacts: billing (full
/// refund), notifications.
/// </summary>
public sealed record OrderCancelled(Guid OrderId, string Reason);
