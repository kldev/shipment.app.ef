using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shipment.Application.Common;
using Shipment.Domain.Catalog;
using Shipment.Domain.Common;
using Shipment.Persistence;

namespace Shipment.Application.Catalog;

public sealed record ProductRow(
    Guid Id,
    Guid PartnerId,
    string PartnerName,
    string Sku,
    string Name,
    string Category,
    string? Description,
    long PriceMinor,
    string Currency,
    int WeightG,
    SizeClass SizeClass,
    bool AgeRestricted,
    bool ListedAvailable,
    DateTimeOffset CreatedAt,
    uint RowVersion
);

/// <summary>A category a partner uses, with how many of its products are listed and hidden.</summary>
public sealed record CategoryRow(Guid PartnerId, string Category, int Listed, int Hidden);

public sealed record CreateProduct(
    Guid Id,
    Guid PartnerId,
    string Sku,
    string Name,
    string Category,
    long PriceMinor,
    int WeightG,
    SizeClass SizeClass,
    bool AgeRestricted = false,
    string? Description = null
);

public sealed record UpdateProduct(
    Guid Id,
    string Sku,
    string Name,
    string Category,
    long PriceMinor,
    int WeightG,
    SizeClass SizeClass,
    bool AgeRestricted,
    bool ListedAvailable,
    uint RowVersion,
    string? Description = null
);

public sealed record SetProductListed(Guid Id, bool ListedAvailable, uint RowVersion);

/// <summary>The catalog context: what partners sell, its price, size and whether customers see it.</summary>
public sealed partial class CatalogService(IDbContextFactory<ShipmentDbContext> factory)
{
    private const string ProductName = "Product";

    public async Task<IReadOnlyList<ProductRow>> ListProductsAsync(
        Guid? partnerId = null,
        string? category = null,
        bool? listedAvailable = null,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var products = db.Products.AsQueryable();
        if (partnerId is { } partner)
            products = products.Where(p => p.PartnerId == partner);
        if (Text.Clean(category) is { } wanted)
            products = products.Where(p => p.Category == wanted);
        if (listedAvailable is { } listed)
            products = products.Where(p => p.ListedAvailable == listed);
        return await Rows(db, products.OrderBy(p => p.Category).ThenBy(p => p.Name).Take(1000))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CategoryRow>> ListCategoriesAsync(
        Guid partnerId,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db
            .Products.Where(p => p.PartnerId == partnerId)
            .GroupBy(p => p.Category)
            .OrderBy(g => g.Key)
            .Select(g => new CategoryRow(
                partnerId,
                g.Key,
                g.Sum(p => p.ListedAvailable ? 1 : 0),
                g.Sum(p => p.ListedAvailable ? 0 : 1)
            ))
            .ToListAsync(ct);
    }

    public Task<Result<uint>> CreateProductAsync(
        CreateProduct command,
        CancellationToken ct = default
    ) => Races.OnceMoreOnConflictAsync(() => CreateProductOnceAsync(command, ct));

    private async Task<Result<uint>> CreateProductOnceAsync(
        CreateProduct command,
        CancellationToken ct
    )
    {
        if (command.Id == Guid.Empty)
            return Error.Validation("Product id is required.");
        var input = Input(
            command.Sku,
            command.Name,
            command.Category,
            command.Description,
            command.PriceMinor,
            command.WeightG,
            command.SizeClass,
            command.AgeRestricted
        );
        if (input.Error is { } invalid)
            return invalid;
        var data = input.Value;

        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Products.FindAsync([command.Id], ct) is { } existing)
            return existing.PartnerId == command.PartnerId && existing.Same(data)
                ? existing.Version
                : Error.Conflict("This id is already used by another product.");
        if (!await db.Partners.AnyAsync(p => p.Id == command.PartnerId, ct))
            return Error.NotFound("Partner not found.");
        if (
            await db.Products.AnyAsync(
                p => p.PartnerId == command.PartnerId && p.Sku == data.Sku,
                ct
            )
        )
            return Error.Conflict("This partner already has a product with this SKU.");

        var product = new Product(command.Id, command.PartnerId, data);
        db.Products.Add(product);
        return await db.SaveAsync(ProductName, ct) is { Error: { } error }
            ? error
            : product.Version;
    }

    public async Task<Result<uint>> UpdateProductAsync(
        UpdateProduct command,
        CancellationToken ct = default
    )
    {
        var input = Input(
            command.Sku,
            command.Name,
            command.Category,
            command.Description,
            command.PriceMinor,
            command.WeightG,
            command.SizeClass,
            command.AgeRestricted
        );
        if (input.Error is { } invalid)
            return invalid;
        var data = input.Value;

        await using var db = await factory.CreateDbContextAsync(ct);
        var product = await db.Products.FindAsync([command.Id], ct);
        if (product is null)
            return Error.NotFound("Product not found.");
        if (!db.Expect(product, command.RowVersion))
            return Error.Stale(ProductName);
        if (
            await db.Products.AnyAsync(
                p => p.PartnerId == product.PartnerId && p.Sku == data.Sku && p.Id != command.Id,
                ct
            )
        )
            return Error.Conflict("This partner already has a product with this SKU.");
        if (!product.Update(data, command.ListedAvailable))
            return product.Version;
        return await db.SaveAsync(ProductName, ct) is { Error: { } error }
            ? error
            : product.Version;
    }

    /// <summary>The catalog tab's quick toggle; picking takes a missing product off through <see cref="Product"/> itself.</summary>
    public async Task<Result<uint>> SetListedAsync(
        SetProductListed command,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var product = await db.Products.FindAsync([command.Id], ct);
        if (product is null)
            return Error.NotFound("Product not found.");
        if (!db.Expect(product, command.RowVersion))
            return Error.Stale(ProductName);
        if (!product.SetListed(command.ListedAvailable))
            return product.Version;
        return await db.SaveAsync(ProductName, ct) is { Error: { } error }
            ? error
            : product.Version;
    }

    private static Result<ProductData> Input(
        string? sku,
        string? name,
        string? category,
        string? description,
        long priceMinor,
        int weightG,
        SizeClass sizeClass,
        bool ageRestricted
    )
    {
        var cleanName = Text.Clean(name);
        if (cleanName is null)
            return Error.Validation("Product name is required.");
        var cleanSku = Text.Clean(sku)?.ToUpperInvariant();
        if (cleanSku is null || !SkuPattern().IsMatch(cleanSku))
            return Error.Validation("SKU must be 2 to 40 letters, digits or dashes.");
        var cleanCategory = Text.Clean(category)?.ToLowerInvariant();
        if (cleanCategory is null || !CategoryPattern().IsMatch(cleanCategory))
            return Error.Validation(
                "Category must be 2 to 40 lower-case letters or underscores, e.g. fruit_veg."
            );
        if (priceMinor is < 1 or > Product.MaxPriceMinor)
            return Error.Validation("Price must be more than 0 and at most 1 000 000 PLN.");
        if (weightG is < 1 or > Product.MaxWeightG)
            return Error.Validation("Weight must be between 1 g and 500 kg.");
        if (!Enum.IsDefined(sizeClass))
            return Error.Validation("Size class must be S, M, L or XL.");
        return new ProductData(
            cleanSku,
            cleanName,
            cleanCategory,
            Text.Clean(description),
            priceMinor,
            weightG,
            sizeClass,
            ageRestricted
        );
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{1,39}$")]
    private static partial Regex SkuPattern();

    [GeneratedRegex("^[a-z][a-z_]{1,39}$")]
    private static partial Regex CategoryPattern();

    // A scalar subquery, not a join: the products keep the order the caller gave them.
    private static IQueryable<ProductRow> Rows(
        ShipmentDbContext db,
        IQueryable<Product> products
    ) =>
        products.Select(p => new ProductRow(
            p.Id,
            p.PartnerId,
            db.Partners.Where(partner => partner.Id == p.PartnerId)
                .Select(partner => partner.Name)
                .First(),
            p.Sku,
            p.Name,
            p.Category,
            p.Description,
            p.PriceMinor,
            Product.Currency,
            p.WeightG,
            p.SizeClass,
            p.AgeRestricted,
            p.ListedAvailable,
            p.CreatedAt,
            p.Version
        ));
}
