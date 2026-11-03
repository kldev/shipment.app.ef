using Shipment.Domain.Common;

namespace Shipment.Domain.Ordering;

/// <summary>A line of an order, copied from the catalog when it was placed; picking decides how many the store had.</summary>
public sealed class OrderLine
{
    public const int MaxQuantity = 99;

    private OrderLine() { }

    internal OrderLine(Guid orderId, int lineNo, OrderProduct product, int quantity)
    {
        OrderId = orderId;
        LineNo = lineNo;
        ProductId = product.Id;
        Sku = product.Sku;
        Name = product.Name;
        UnitPriceMinor = product.PriceMinor;
        Quantity = quantity;
        WeightG = product.WeightG;
        SizeClass = product.SizeClass;
        AgeRestricted = product.AgeRestricted;
    }

    public Guid OrderId { get; private set; }

    public int LineNo { get; private set; }

    public Guid ProductId { get; private set; }

    public string Sku { get; private set; } = "";

    public string Name { get; private set; } = "";

    public long UnitPriceMinor { get; private set; }

    public int Quantity { get; private set; }

    public int WeightG { get; private set; }

    public SizeClass SizeClass { get; private set; }

    public bool AgeRestricted { get; private set; }

    /// <summary>How many the store had; null until picking ends.</summary>
    public int? PickedQuantity { get; private set; }

    internal void Pick(int pickedQuantity) => PickedQuantity = pickedQuantity;
}

/// <summary>What an order copies from a catalog product.</summary>
public sealed record OrderProduct(
    Guid Id,
    string Sku,
    string Name,
    long PriceMinor,
    int WeightG,
    SizeClass SizeClass,
    bool AgeRestricted
);
