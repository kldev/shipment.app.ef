using Shipment.Domain.Common;

namespace Shipment.Domain.Ordering;

public enum OrderStatus
{
    Placed,
    Picking,
    Ready,
    Cancelled,
    Delivered,
    Failed,
}

/// <summary>The customer and the address an order goes to, as copied at placing.</summary>
public sealed record OrderDropoff(
    Guid CustomerId,
    string CustomerName,
    Guid AddressId,
    string Label,
    string AddressLine,
    string City,
    string Postcode,
    string? Notes,
    decimal Latitude,
    decimal Longitude
);

/// <summary>The partner and the pickup point an order comes from, as copied at placing.</summary>
public sealed record OrderPickup(
    Guid PartnerId,
    string PartnerName,
    Guid PickupPointId,
    string Name,
    string AddressLine,
    decimal Latitude,
    decimal Longitude
);

/// <summary>How many of a line the store packed.</summary>
public sealed record LinePick(int LineNo, int PickedQuantity, bool TakeOffCatalog);

/// <summary>
/// A customer's order from one partner's pickup point. It copies what it needs when it is placed (names, prices,
/// sizes, addresses, coordinates): later changes in crm or catalog never change a placed order. Money in grosze.
/// </summary>
public sealed class Order : Audited
{
    public const string Currency = "PLN";
    public const string NothingAvailable = "None of the products was available.";
    public static readonly IReadOnlyList<int> TipPercents = [0, 5, 10];

    private readonly List<OrderLine> _lines = [];

    private Order() { }

    public Order(
        Guid id,
        string code,
        OrderDropoff dropoff,
        OrderPickup pickup,
        IReadOnlyList<(OrderProduct Product, int Quantity)> lines,
        int distanceM,
        long deliveryFeeMinor,
        int tipPercent,
        DateTimeOffset now
    )
    {
        Id = id;
        Code = code;
        CustomerId = dropoff.CustomerId;
        CustomerName = dropoff.CustomerName;
        AddressId = dropoff.AddressId;
        AddressLabel = dropoff.Label;
        AddressLine = dropoff.AddressLine;
        AddressCity = dropoff.City;
        AddressPostcode = dropoff.Postcode;
        AddressNotes = dropoff.Notes;
        AddressLatitude = dropoff.Latitude;
        AddressLongitude = dropoff.Longitude;
        PartnerId = pickup.PartnerId;
        PartnerName = pickup.PartnerName;
        PickupPointId = pickup.PickupPointId;
        PickupPointName = pickup.Name;
        PickupAddressLine = pickup.AddressLine;
        PickupLatitude = pickup.Latitude;
        PickupLongitude = pickup.Longitude;
        DistanceM = distanceM;
        DeliveryFeeMinor = deliveryFeeMinor;
        for (var i = 0; i < lines.Count; i++)
            _lines.Add(new OrderLine(id, i + 1, lines[i].Product, lines[i].Quantity));
        GoodsOrderedMinor = _lines.Sum(l => l.UnitPriceMinor * l.Quantity);
        TipPercent = tipPercent;
        TipMinor = (long)
            Math.Round(GoodsOrderedMinor * tipPercent / 100m, MidpointRounding.AwayFromZero);
        Status = OrderStatus.Placed;
        PlacedAt = now;
    }

    public Guid Id { get; private set; }

    /// <summary>ORD-104001, from a sequence.</summary>
    public string Code { get; private set; } = "";

    public Guid CustomerId { get; private set; }

    public string CustomerName { get; private set; } = "";

    public Guid AddressId { get; private set; }

    public string AddressLabel { get; private set; } = "";

    public string AddressLine { get; private set; } = "";

    public string AddressCity { get; private set; } = "";

    public string AddressPostcode { get; private set; } = "";

    public string? AddressNotes { get; private set; }

    public decimal AddressLatitude { get; private set; }

    public decimal AddressLongitude { get; private set; }

    public Guid PartnerId { get; private set; }

    public string PartnerName { get; private set; } = "";

    public Guid PickupPointId { get; private set; }

    public string PickupPointName { get; private set; } = "";

    public string PickupAddressLine { get; private set; } = "";

    public decimal PickupLatitude { get; private set; }

    public decimal PickupLongitude { get; private set; }

    public int DistanceM { get; private set; }

    public long DeliveryFeeMinor { get; private set; }

    public long GoodsOrderedMinor { get; private set; }

    /// <summary>What the store packed; null until picking ends.</summary>
    public long? GoodsPickedMinor { get; private set; }

    /// <summary>The customer's tip for the courier, 0, 5 or 10 % of the goods as ordered - missing items do not lower it.</summary>
    public int TipPercent { get; private set; }

    public long TipMinor { get; private set; }

    public OrderStatus Status { get; private set; }

    public string? CancelReason { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>The admin who started picking (acting for the store).</summary>
    public Guid? PickedBy { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public DateTimeOffset? PickingStartedAt { get; private set; }

    public DateTimeOffset? ReadyAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    /// <summary>A retried placing: the same customer, address, point and lines (in any order).</summary>
    public bool Same(
        Guid customerId,
        Guid addressId,
        Guid pickupPointId,
        IReadOnlyCollection<(Guid ProductId, int Quantity)> lines
    ) =>
        CustomerId == customerId
        && AddressId == addressId
        && PickupPointId == pickupPointId
        && _lines.Count == lines.Count
        && _lines.All(l => lines.Contains((l.ProductId, l.Quantity)));

    /// <summary>placed -> picking; already picking is a no-op.</summary>
    public Result<bool> StartPicking(Guid? by, DateTimeOffset now)
    {
        if (Status == OrderStatus.Picking)
            return false;
        if (Status != OrderStatus.Placed)
            return Error.Conflict("This order is not waiting to be packed.");
        Status = OrderStatus.Picking;
        PickingStartedAt = now;
        PickedBy = by;
        return true;
    }

    /// <summary>Only before the store starts packing, with a reason for the customer.</summary>
    public Result Cancel(string reason, DateTimeOffset now)
    {
        if (Status != OrderStatus.Placed)
            return Error.Conflict(
                "Only an order the store has not started packing can be cancelled."
            );
        Status = OrderStatus.Cancelled;
        CancelReason = reason;
        CancelledAt = now;
        return Result.Success;
    }

    /// <summary>
    /// The store finished packing: every line decided once, 0..ordered. Something packed -> ready; nothing -> cancelled.
    /// </summary>
    public Result<OrderStatus> FinishPicking(
        IReadOnlyCollection<LinePick> picks,
        DateTimeOffset now
    )
    {
        if (Status != OrderStatus.Picking)
            return Error.Conflict("Start picking the order first - it is not being packed.");
        var lineNos = picks.Select(p => p.LineNo).ToList();
        if (
            lineNos.Distinct().Count() != lineNos.Count
            || !lineNos.Order().SequenceEqual(_lines.Select(l => l.LineNo).Order())
        )
            return Error.Validation("Decide every line of the order once: packed or missing.");
        if (picks.Any(p => p.PickedQuantity < 0 || p.PickedQuantity > Line(p.LineNo).Quantity))
            return Error.Validation("Packed quantity is between 0 and the ordered quantity.");

        foreach (var pick in picks)
            Line(pick.LineNo).Pick(pick.PickedQuantity);
        GoodsPickedMinor = _lines.Sum(l => l.UnitPriceMinor * l.PickedQuantity!.Value);
        if (GoodsPickedMinor == 0)
        {
            Status = OrderStatus.Cancelled;
            CancelReason = NothingAvailable;
            CancelledAt = now;
        }
        else
        {
            Status = OrderStatus.Ready;
            ReadyAt = now;
        }
        return Status;
    }

    /// <summary>
    /// The shipment ended: delivered or failed with the courier's reason. Only a ready order moves; one that ended
    /// this way already is a no-op (a repeated event).
    /// </summary>
    public Result<bool> Complete(bool delivered, string? reason, DateTimeOffset now)
    {
        var target = delivered ? OrderStatus.Delivered : OrderStatus.Failed;
        if (Status == target)
            return false;
        if (Status != OrderStatus.Ready)
            return Error.Conflict(
                $"Order {Code} is {Status.ToString().ToLowerInvariant()}, not ready - it cannot be {target.ToString().ToLowerInvariant()}."
            );
        Status = target;
        if (delivered)
            DeliveredAt = now;
        else
        {
            FailedAt = now;
            FailureReason = Truncate(reason ?? "Delivery failed.", 500);
        }
        return true;
    }

    private OrderLine Line(int lineNo) => _lines.First(l => l.LineNo == lineNo);

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
