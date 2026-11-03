using System.Net;
using System.Text.Json.Nodes;
using Shipment.Api.Endpoints;

namespace Shipment.Tests;

/// <summary>Module catalog: what a partner sells and what the app lists as available (spec 001).</summary>
[Trait("verify", "catalog")]
[Collection(ApiCollection.Name)]
public sealed class CatalogTests(ApiFixture api)
{
    private static readonly HttpMethod Post = HttpMethod.Post;
    private static readonly HttpMethod Put = HttpMethod.Put;

    private static object Product(Guid id, Guid partnerId, string sku, long priceMinor = 1299) =>
        new
        {
            id,
            partnerId,
            sku,
            name = "Craft beer 0.5 l",
            category = "alcohol",
            priceMinor,
            weightG = 900,
            sizeClass = "S",
            ageRestricted = true,
        };

    /// <summary>One product as the list shows it (there is no GET of a single product).</summary>
    internal static async Task<JsonObject> ReadProductAsync(
        HttpClient admin,
        Guid partnerId,
        Guid id
    ) =>
        Assert
            .Single(
                await admin.ListAsync($"{ApiRoutes.Products}?partnerId={partnerId}"),
                p => p!["id"]!.GetValue<Guid>() == id
            )!
            .AsObject();

    private async Task<(HttpClient Admin, Guid PartnerId)> PartnerAsync()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var partnerId = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.Partners,
            new
            {
                id = partnerId,
                code = $"C{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
                name = "Catalog Test Store",
                kind = "grocery",
                commissionBps = 1000,
            }
        );
        return (admin, partnerId);
    }

    [Fact]
    public async Task A_product_is_listed_when_created_and_its_sku_is_unique_within_the_partner()
    {
        var (admin, partnerId) = await PartnerAsync();
        var id = Guid.CreateVersion7();

        var created = await admin.PostAsync(ApiRoutes.Products, Product(id, partnerId, "beer-1"));
        Assert.Equal(
            created.RowVersion(),
            (
                await admin.PostAsync(ApiRoutes.Products, Product(id, partnerId, "BEER-1"))
            ).RowVersion()
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Products,
            Product(id, partnerId, "BEER-1", priceMinor: 1),
            HttpStatusCode.Conflict,
            "already used by another product"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Products,
            Product(Guid.CreateVersion7(), partnerId, "BEER-1"),
            HttpStatusCode.Conflict,
            "already has a product with this SKU"
        );

        var read = await ReadProductAsync(admin, partnerId, id);
        Assert.Equal("BEER-1", read["sku"]!.GetValue<string>());
        Assert.True(read["listedAvailable"]!.GetValue<bool>());
        Assert.True(read["ageRestricted"]!.GetValue<bool>());
        Assert.Equal("S", read["sizeClass"]!.GetValue<string>());
        Assert.Equal("Catalog Test Store", read["partnerName"]!.GetValue<string>());

        // Another partner may use the same SKU.
        var (_, otherPartner) = await PartnerAsync();
        await admin.PostAsync(
            ApiRoutes.Products,
            Product(Guid.CreateVersion7(), otherPartner, "BEER-1")
        );
    }

    [Fact]
    public async Task Product_rules_are_refused_with_their_messages()
    {
        var (admin, partnerId) = await PartnerAsync();
        object Body(string sku, string category, object sizeClass) =>
            new
            {
                id = Guid.CreateVersion7(),
                partnerId,
                sku,
                name = "Sofa",
                category,
                priceMinor = 100,
                weightG = 100,
                sizeClass,
            };
        var cases = new (object Body, HttpStatusCode Status, string Message)[]
        {
            (
                Product(Guid.CreateVersion7(), partnerId, "a"),
                HttpStatusCode.BadRequest,
                "SKU must be 2 to 40 letters"
            ),
            (
                Product(Guid.CreateVersion7(), partnerId, "OK-1", priceMinor: 0),
                HttpStatusCode.BadRequest,
                "Price must be more than 0"
            ),
            (
                Product(Guid.CreateVersion7(), Guid.CreateVersion7(), "OK-2"),
                HttpStatusCode.NotFound,
                "Partner not found"
            ),
            (
                Body("OK-3", "Furniture!", "S"),
                HttpStatusCode.BadRequest,
                "Category must be 2 to 40"
            ),
        };
        foreach (var (body, status, message) in cases)
            await admin.RefusedAsync(Post, ApiRoutes.Products, body, status, message);
        // An unknown size name does not bind.
        await admin.NotBoundAsync(Post, ApiRoutes.Products, Body("OK-5", "furniture", "xxl"));
    }

    [Fact]
    public async Task Hiding_a_product_changes_the_listing_and_a_stale_version_is_a_conflict()
    {
        var (admin, partnerId) = await PartnerAsync();
        var id = Guid.CreateVersion7();
        var created = await admin.PostAsync(ApiRoutes.Products, Product(id, partnerId, "HIDE-1"));

        var hidden = await admin.PutAsync(
            ApiRoutes.ProductListed.With(id),
            new { listedAvailable = false, rowVersion = created.RowVersion() }
        );
        Assert.NotEqual(created.RowVersion(), hidden.RowVersion());
        // Hidden already: a no-op, the version stays.
        Assert.Equal(
            hidden.RowVersion(),
            (
                await admin.PutAsync(
                    ApiRoutes.ProductListed.With(id),
                    new { listedAvailable = false, rowVersion = hidden.RowVersion() }
                )
            ).RowVersion()
        );
        await admin.RefusedAsync(
            Put,
            ApiRoutes.ProductListed.With(id),
            new { listedAvailable = true, rowVersion = created.RowVersion() },
            HttpStatusCode.Conflict,
            "was changed by someone else"
        );

        Assert.Empty(
            await admin.ListAsync(
                $"{ApiRoutes.Products}?partnerId={partnerId}&listedAvailable=true"
            )
        );
        var category = Assert.Single(
            await admin.ListAsync(ApiRoutes.PartnerCategories.With(partnerId))
        );
        Assert.Equal("alcohol", category!["category"]!.GetValue<string>());
        Assert.Equal(0, category["listed"]!.GetValue<int>());
        Assert.Equal(1, category["hidden"]!.GetValue<int>());

        var edited = await admin.PutAsync(
            ApiRoutes.Product.With(id),
            new
            {
                sku = "HIDE-1",
                name = "Craft beer 0.5 l",
                category = "alcohol",
                priceMinor = 1499,
                weightG = 900,
                sizeClass = "S",
                ageRestricted = true,
                listedAvailable = true,
                rowVersion = hidden.RowVersion(),
            }
        );
        Assert.NotEqual(hidden.RowVersion(), edited.RowVersion());
        var read = await ReadProductAsync(admin, partnerId, id);
        Assert.Equal(1499, read["priceMinor"]!.GetValue<long>());
        Assert.True(read["listedAvailable"]!.GetValue<bool>());
    }

    [Fact]
    public async Task The_remaining_catalog_rules_are_refused_with_their_messages()
    {
        var (admin, partnerId) = await PartnerAsync();
        object Body(Guid id, string name, int weightG) =>
            new
            {
                id,
                partnerId,
                sku = "RULE-1",
                name,
                category = "snacks",
                priceMinor = 100,
                weightG,
                sizeClass = "S",
            };
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Products,
            Body(Guid.Empty, "Crisps", 100),
            HttpStatusCode.BadRequest,
            "Product id is required"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Products,
            Body(Guid.CreateVersion7(), " ", 100),
            HttpStatusCode.BadRequest,
            "Product name is required"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Products,
            Body(Guid.CreateVersion7(), "Crisps", 0),
            HttpStatusCode.BadRequest,
            "Weight must be between 1 g and 500 kg"
        );
        await admin.RefusedAsync(
            Put,
            ApiRoutes.ProductListed.With(Guid.CreateVersion7()),
            new { listedAvailable = true, rowVersion = 1 },
            HttpStatusCode.NotFound,
            "Product not found"
        );
        var update = new
        {
            sku = "RULE-1",
            name = "Crisps",
            category = "snacks",
            priceMinor = 100,
            weightG = 100,
            sizeClass = "S",
            ageRestricted = false,
            listedAvailable = true,
            rowVersion = 1,
        };
        await admin.RefusedAsync(
            Put,
            ApiRoutes.Product.With(Guid.CreateVersion7()),
            update,
            HttpStatusCode.NotFound,
            "Product not found"
        );
        // ageRestricted and listedAvailable are plain booleans: null does not bind.
        await admin.NotBoundAsync(
            Put,
            ApiRoutes.Product.With(Guid.CreateVersion7()),
            new
            {
                sku = "RULE-1",
                name = "Crisps",
                category = "snacks",
                priceMinor = 100,
                weightG = 100,
                sizeClass = "S",
                ageRestricted = (bool?)null,
                listedAvailable = true,
                rowVersion = 1,
            }
        );
    }
}
