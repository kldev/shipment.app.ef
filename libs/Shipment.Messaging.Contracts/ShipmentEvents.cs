namespace Shipment.Messaging.Contracts;

/// <summary>
/// A courier took a shipment (DispatchService.AssignAsync). Reacts: tracking (the route from the courier to the pickup
/// point and on to the customer - spec 005).
/// </summary>
public sealed record CourierAssigned(Guid ShipmentId, Guid OrderId, string Code, Guid CourierId);

/// <summary>
/// A shipment moved to <see cref="Status"/> - its snake_case name (available again after an unassign, courier_assigned, arrived_at_pickup,
/// picked_up, arrived_at_customer, delivered, failed). Reason only for failed. Reacts: ordering (the order delivered or
/// failed), tracking (the timeline - spec 005).
/// </summary>
public sealed record ShipmentStatusChanged(
    Guid ShipmentId,
    Guid OrderId,
    string Code,
    string Status,
    string? Reason
);

/// <summary>
/// The customer has the goods (DispatchService.AdvanceAsync to delivered). Reacts: billing (commission, the courier's
/// payout - spec 006), notifications (the delivered e-mail - spec 007).
/// </summary>
public sealed record ShipmentDelivered(Guid ShipmentId, Guid OrderId, string Code, Guid CourierId);
