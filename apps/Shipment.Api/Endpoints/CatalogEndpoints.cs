using Shipment.Application.Catalog;
using Shipment.Domain.Common;

namespace Shipment.Api.Endpoints;

internal sealed class CatalogEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags(ApiTags.Catalog);

        group
            .MapGet(
                ApiRoutes.Products,
                (
                    Guid? partnerId,
                    string? category,
                    bool? listedAvailable,
                    CatalogService catalog,
                    CancellationToken ct
                ) => catalog.ListProductsAsync(partnerId, category, listedAvailable, ct)
            )
            .WithName("Get products")
            .WithSummary("Products by partner, category and whether customers see them");
        group
            .MapGet(
                ApiRoutes.PartnerCategories,
                (Guid partnerId, CatalogService catalog, CancellationToken ct) =>
                    catalog.ListCategoriesAsync(partnerId, ct)
            )
            .WithName("Get partner categories")
            .WithSummary("The categories a partner uses, with listed and hidden counts");
        group
            .MapPost(
                ApiRoutes.Products,
                async (CreateProduct request, CatalogService catalog, CancellationToken ct) =>
                    ApiResults.Created(
                        (await catalog.CreateProductAsync(request, ct)).AsSaved(),
                        $"{ApiRoutes.Products}/{request.Id}"
                    )
            )
            .WithName("Create product")
            .WithSummary("Add a product, listed as available; the SKU is unique within the partner")
            .ProducesRefusals();
        group
            .MapPut(
                ApiRoutes.Product,
                async (
                    Guid productId,
                    ProductChange request,
                    CatalogService catalog,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await catalog.UpdateProductAsync(
                                new UpdateProduct(
                                    productId,
                                    request.Sku,
                                    request.Name,
                                    request.Category,
                                    request.PriceMinor,
                                    request.WeightG,
                                    request.SizeClass,
                                    request.AgeRestricted,
                                    request.ListedAvailable,
                                    request.RowVersion,
                                    request.Description
                                ),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Update product")
            .WithSummary("Edit a product; a new price applies to orders placed from now on")
            .ProducesRefusals();
        group
            .MapPut(
                ApiRoutes.ProductListed,
                async (
                    Guid productId,
                    ListedChange request,
                    CatalogService catalog,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await catalog.SetListedAsync(
                                new SetProductListed(
                                    productId,
                                    request.ListedAvailable,
                                    request.RowVersion
                                ),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Change product listed")
            .WithSummary("Show or hide a product in the catalog")
            .ProducesRefusals();
    }

    internal sealed record ProductChange(
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

    internal sealed record ListedChange(bool ListedAvailable, uint RowVersion);
}
