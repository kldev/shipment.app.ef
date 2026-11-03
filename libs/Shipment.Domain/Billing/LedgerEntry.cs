namespace Shipment.Domain.Billing;

public enum EntryKind
{
    Authorization,
    CaptureGoods,
    CaptureFee,
    CaptureTip,
    Refund,
    Commission,
    Payout,
    Tip,
}

/// <summary>
/// An entry of the append-only ledger (billing.entries): a correction is a new entry, amounts are never updated. One
/// entry of a kind per order; refunds one per missing line, or one for the whole order (unique indexes).
/// </summary>
public sealed class LedgerEntry
{
    public const string Currency = "PLN";

    private LedgerEntry() { }

    public LedgerEntry(
        Guid eventId,
        EntryKind kind,
        Guid orderId,
        string orderCode,
        Guid partnerId,
        long amountMinor,
        string description,
        DateTimeOffset bookedAt
    )
    {
        EventId = eventId;
        Kind = kind;
        OrderId = orderId;
        OrderCode = orderCode;
        PartnerId = partnerId;
        AmountMinor = Math.Max(amountMinor, 0);
        Description = description.Length <= 200 ? description : description[..200];
        BookedAt = bookedAt;
    }

    public long Id { get; private set; }

    public Guid EventId { get; private set; }

    public EntryKind Kind { get; private set; }

    public Guid OrderId { get; private set; }

    public string OrderCode { get; private set; } = "";

    public Guid PartnerId { get; private set; }

    public Guid? ShipmentId { get; private set; }

    public Guid? CourierId { get; private set; }

    /// <summary>A refund of a missing line: the line, the product, how many.</summary>
    public int? LineNo { get; private set; }

    public Guid? ProductId { get; private set; }

    public int? Quantity { get; private set; }

    /// <summary>What the amount was computed from: the packed goods for a commission, the route in metres for a payout.</summary>
    public long? Basis { get; private set; }

    public long AmountMinor { get; private set; }

    public DateTimeOffset BookedAt { get; private set; }

    public string Description { get; private set; } = "";

    public LedgerEntry ForDelivery(Guid shipmentId, Guid? courierId, long? basis)
    {
        (ShipmentId, CourierId, Basis) = (shipmentId, courierId, basis);
        return this;
    }

    public LedgerEntry ForLine(int lineNo, Guid productId, int quantity)
    {
        (LineNo, ProductId, Quantity) = (lineNo, productId, quantity);
        return this;
    }
}
