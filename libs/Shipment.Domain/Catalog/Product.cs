using Shipment.Domain.Common;

namespace Shipment.Domain.Catalog;

/// <summary>The data of a product a form sends (create or edit).</summary>
public sealed record ProductData(
    string Sku,
    string Name,
    string Category,
    string? Description,
    long PriceMinor,
    int WeightG,
    SizeClass SizeClass,
    bool AgeRestricted
);

/// <summary>
/// What a partner sells. "Listed available" is what customers see - it may be wrong about the shelf; picking finds out.
/// Size class and weight choose the vehicle; age restricted products need an ID check at the door. Money in grosze.
/// </summary>
public sealed class Product : Audited
{
    public const string Currency = "PLN";
    public const long MaxPriceMinor = 100_000_000;
    public const int MaxWeightG = 500_000;

    private Product() { }

    public Product(Guid id, Guid partnerId, ProductData data)
    {
        Id = id;
        PartnerId = partnerId;
        Set(data);
        ListedAvailable = true;
    }

    public Guid Id { get; private set; }

    public Guid PartnerId { get; private set; }

    public string Sku { get; private set; } = "";

    public string Name { get; private set; } = "";

    /// <summary>lower_case, e.g. fruit_veg.</summary>
    public string Category { get; private set; } = "";

    public string? Description { get; private set; }

    public long PriceMinor { get; private set; }

    public int WeightG { get; private set; }

    public SizeClass SizeClass { get; private set; }

    public bool AgeRestricted { get; private set; }

    public bool ListedAvailable { get; private set; }

    public bool Same(ProductData data) =>
        Sku == data.Sku
        && Name == data.Name
        && Category == data.Category
        && Description == data.Description
        && PriceMinor == data.PriceMinor
        && WeightG == data.WeightG
        && SizeClass == data.SizeClass
        && AgeRestricted == data.AgeRestricted;

    /// <summary>A new price applies to orders placed from now on - an order keeps its own copy.</summary>
    public bool Update(ProductData data, bool listedAvailable)
    {
        if (Same(data) && ListedAvailable == listedAvailable)
            return false;
        Set(data);
        ListedAvailable = listedAvailable;
        return true;
    }

    public bool SetListed(bool listedAvailable)
    {
        if (ListedAvailable == listedAvailable)
            return false;
        ListedAvailable = listedAvailable;
        return true;
    }

    private void Set(ProductData data)
    {
        Sku = data.Sku;
        Name = data.Name;
        Category = data.Category;
        Description = data.Description;
        PriceMinor = data.PriceMinor;
        WeightG = data.WeightG;
        SizeClass = data.SizeClass;
        AgeRestricted = data.AgeRestricted;
    }
}
