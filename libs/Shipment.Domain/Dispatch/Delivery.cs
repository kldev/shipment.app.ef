using Shipment.Domain.Common;

namespace Shipment.Domain.Dispatch;

public enum ShipmentStatus
{
    Available,
    CourierAssigned,
    ArrivedAtPickup,
    PickedUp,
    ArrivedAtCustomer,
    Delivered,
    Failed,
}

/// <summary>What a shipment copies from the order it ships (dispatch never reads ordering's tables later).</summary>
public sealed record DeliveryTrip(
    Guid OrderId,
    string OrderCode,
    Guid CustomerId,
    string CustomerName,
    string PartnerName,
    Guid PickupPointId,
    string PickupPointName,
    string PickupAddressLine,
    decimal PickupLatitude,
    decimal PickupLongitude,
    string DropoffLabel,
    string DropoffAddressLine,
    string DropoffCity,
    string? DropoffNotes,
    decimal DropoffLatitude,
    decimal DropoffLongitude,
    int DistanceM,
    int ItemCount,
    int WeightG,
    SizeClass LargestSizeClass,
    bool AgeCheckRequired
);

/// <summary>
/// A shipment (dispatch.shipments): what the store packed, from its pickup point to the customer's door, and the
/// courier who takes it. One per order. The class is not called Shipment - that is the root namespace.
/// </summary>
public sealed class Delivery : Audited
{
    /// <summary>The courier's steps, one at a time and in this order.</summary>
    private static readonly ShipmentStatus[] Steps =
    [
        ShipmentStatus.CourierAssigned,
        ShipmentStatus.ArrivedAtPickup,
        ShipmentStatus.PickedUp,
        ShipmentStatus.ArrivedAtCustomer,
        ShipmentStatus.Delivered,
    ];

    private Delivery() { }

    public Delivery(Guid id, string code, DeliveryTrip trip, string requiredVehicle)
    {
        Id = id;
        Code = code;
        OrderId = trip.OrderId;
        OrderCode = trip.OrderCode;
        CustomerId = trip.CustomerId;
        CustomerName = trip.CustomerName;
        PartnerName = trip.PartnerName;
        PickupPointId = trip.PickupPointId;
        PickupPointName = trip.PickupPointName;
        PickupAddressLine = trip.PickupAddressLine;
        PickupLatitude = trip.PickupLatitude;
        PickupLongitude = trip.PickupLongitude;
        DropoffLabel = trip.DropoffLabel;
        DropoffAddressLine = trip.DropoffAddressLine;
        DropoffCity = trip.DropoffCity;
        DropoffNotes = trip.DropoffNotes;
        DropoffLatitude = trip.DropoffLatitude;
        DropoffLongitude = trip.DropoffLongitude;
        DistanceM = trip.DistanceM;
        ItemCount = trip.ItemCount;
        WeightG = trip.WeightG;
        LargestSizeClass = trip.LargestSizeClass;
        AgeCheckRequired = trip.AgeCheckRequired;
        RequiredVehicle = requiredVehicle;
        Status = ShipmentStatus.Available;
    }

    public Guid Id { get; private set; }

    /// <summary>SHP-204001, from a sequence.</summary>
    public string Code { get; private set; } = "";

    public Guid OrderId { get; private set; }

    public string OrderCode { get; private set; } = "";

    public Guid CustomerId { get; private set; }

    public string CustomerName { get; private set; } = "";

    public string PartnerName { get; private set; } = "";

    public Guid PickupPointId { get; private set; }

    public string PickupPointName { get; private set; } = "";

    public string PickupAddressLine { get; private set; } = "";

    public decimal PickupLatitude { get; private set; }

    public decimal PickupLongitude { get; private set; }

    public string DropoffLabel { get; private set; } = "";

    public string DropoffAddressLine { get; private set; } = "";

    public string DropoffCity { get; private set; } = "";

    public string? DropoffNotes { get; private set; }

    public decimal DropoffLatitude { get; private set; }

    public decimal DropoffLongitude { get; private set; }

    public int DistanceM { get; private set; }

    public int ItemCount { get; private set; }

    public int WeightG { get; private set; }

    public SizeClass LargestSizeClass { get; private set; }

    /// <summary>The smallest vehicle type that carries the freight.</summary>
    public string RequiredVehicle { get; private set; } = "";

    /// <summary>18+ products: the courier checks the customer's ID at the door.</summary>
    public bool AgeCheckRequired { get; private set; }

    public bool AgeChecked { get; private set; }

    public ShipmentStatus Status { get; private set; }

    public Guid? CourierId { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset? AssignedAt { get; private set; }

    public DateTimeOffset? ArrivedAtPickupAt { get; private set; }

    public DateTimeOffset? PickedUpAt { get; private set; }

    public DateTimeOffset? ArrivedAtCustomerAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public bool IsActive => Status is not (ShipmentStatus.Delivered or ShipmentStatus.Failed);

    public Result Assign(Guid courierId, DateTimeOffset now)
    {
        if (Status != ShipmentStatus.Available)
            return Error.Conflict("The shipment already has a courier.");
        Status = ShipmentStatus.CourierAssigned;
        CourierId = courierId;
        AssignedAt = now;
        return Result.Success;
    }

    /// <summary>The courier's next step; delivered needs the ID check confirmed when the freight has 18+ products.</summary>
    public Result Advance(ShipmentStatus next, bool ageChecked, DateTimeOffset now)
    {
        if (Array.IndexOf(Steps, next) < 1)
            return Error.Validation(
                "The next step is one of: arrived at pickup, picked up, arrived at customer, delivered."
            );
        var current = Array.IndexOf(Steps, Status);
        var expected =
            current >= 0 && current + 1 < Steps.Length ? Steps[current + 1] : (ShipmentStatus?)null;
        if (expected != next)
            return Error.Conflict(
                Status == ShipmentStatus.Available ? "Assign a courier first."
                : expected is null ? "The shipment has ended."
                : $"The next step is {Describe(expected.Value)}."
            );
        if (next == ShipmentStatus.Delivered && AgeCheckRequired && !ageChecked)
            return Error.Validation(
                "The order has 18+ products: confirm that you checked the customer's ID."
            );

        Status = next;
        switch (next)
        {
            case ShipmentStatus.ArrivedAtPickup:
                ArrivedAtPickupAt = now;
                break;
            case ShipmentStatus.PickedUp:
                PickedUpAt = now;
                break;
            case ShipmentStatus.ArrivedAtCustomer:
                ArrivedAtCustomerAt = now;
                break;
            case ShipmentStatus.Delivered:
                DeliveredAt = now;
                AgeChecked = AgeChecked || AgeCheckRequired;
                break;
        }
        return Result.Success;
    }

    /// <summary>Only once the courier has the goods (before that: unassign). It ends the order - no second courier.</summary>
    public Result Fail(string reason, DateTimeOffset now)
    {
        if (Status is not (ShipmentStatus.PickedUp or ShipmentStatus.ArrivedAtCustomer))
            return Error.Conflict(
                "Only a shipment the courier has picked up can fail - before that, unassign the courier."
            );
        Status = ShipmentStatus.Failed;
        FailureReason = reason;
        FailedAt = now;
        return Result.Success;
    }

    /// <summary>Before the courier has the goods: available again, the courier free.</summary>
    public Result Unassign()
    {
        if (Status is not (ShipmentStatus.CourierAssigned or ShipmentStatus.ArrivedAtPickup))
            return Error.Conflict(
                "Only a courier who has not picked the goods up yet can be taken off the shipment."
            );
        Status = ShipmentStatus.Available;
        CourierId = null;
        AssignedAt = null;
        ArrivedAtPickupAt = null;
        return Result.Success;
    }

    /// <summary>"arrived at pickup" - the wire name with spaces.</summary>
    private static string Describe(ShipmentStatus status) =>
        status switch
        {
            ShipmentStatus.CourierAssigned => "courier assigned",
            ShipmentStatus.ArrivedAtPickup => "arrived at pickup",
            ShipmentStatus.PickedUp => "picked up",
            ShipmentStatus.ArrivedAtCustomer => "arrived at customer",
            _ => status.ToString().ToLowerInvariant(),
        };
}
