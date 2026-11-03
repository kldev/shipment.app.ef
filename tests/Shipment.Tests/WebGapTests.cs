using System.Net;
using System.Text.Json.Nodes;
using Shipment.Api.Endpoints;

namespace Shipment.Tests;

/// <summary>What the rewrite's REST API adds: the OpenAPI document without /auth, sizes and enums on the wire.</summary>
[Trait("verify", "web")]
[Collection(ApiCollection.Name)]
public sealed class WebGapTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_openapi_document_has_only_the_versioned_api_and_no_auth_paths()
    {
        var doc = JsonNode.Parse(
            await api.CreateClient().GetStringAsync($"/openapi/{ApiRoutes.Document}.json", Ct)
        )!;
        var paths = doc["paths"]!.AsObject().Select(p => p.Key).ToList();
        Assert.NotEmpty(paths);
        Assert.DoesNotContain(paths, p => p.StartsWith("/auth", StringComparison.Ordinal));
        Assert.All(paths, p => Assert.StartsWith("/api/v1/", p));
    }

    [Fact]
    public async Task Sizes_are_written_as_s_m_l_xl_and_numbers_do_not_bind()
    {
        var shop = await GapShop.CreateAsync(api);
        var sofa = await shop.ProductAsync("SOFA", 99900, weightG: 60000, sizeClass: "XL");
        var listed = Assert.Single(
            await shop.Admin.ListAsync($"{ApiRoutes.Products}?partnerId={shop.PartnerId}"),
            p => p!["id"]!.GetValue<Guid>() == sofa
        )!;
        Assert.Equal("XL", listed["sizeClass"]!.GetValue<string>());
        Assert.Equal(
            "XL",
            await api.ScalarAsync<string>(
                $"SELECT size_class::text FROM catalog.products WHERE id = '{sofa}'"
            )
        );
        await shop.Admin.NotBoundAsync(
            HttpMethod.Post,
            ApiRoutes.Products,
            new
            {
                id = Guid.CreateVersion7(),
                partnerId = shop.PartnerId,
                sku = "NUM-1",
                name = "Number size",
                category = "groceries",
                priceMinor = 100,
                weightG = 100,
                sizeClass = 4,
            }
        );
        var vehicleSizes = (await shop.Admin.ListAsync(ApiRoutes.VehicleTypes))
            .Select(v => v!["maxSizeClass"]!.GetValue<string>())
            .ToList();
        Assert.All(vehicleSizes, s => Assert.Contains(s, new[] { "S", "M", "L", "XL" }));
    }

    [Fact]
    public async Task Enum_query_values_are_the_wire_names_for_every_filter()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        foreach (
            var (path, good) in new[]
            {
                (ApiRoutes.Orders, "cancelled"),
                (ApiRoutes.Shipments, "arrived_at_customer"),
                (ApiRoutes.PickupPoints, "active"),
                (ApiRoutes.CourierDocuments, "expiring"),
                (ApiRoutes.BillingEntries, "capture_goods"),
            }
        )
        {
            var param =
                path == ApiRoutes.BillingEntries ? "kind"
                : path == ApiRoutes.CourierDocuments ? "state"
                : "status";
            Assert.Equal(HttpStatusCode.OK, await admin.GetStatusAsync($"{path}?{param}={good}"));
            Assert.Equal(
                HttpStatusCode.BadRequest,
                await admin.GetStatusAsync($"{path}?{param}=nope")
            );
        }
    }

    [Fact]
    public async Task Unknown_ids_in_reads_are_404_not_500()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        foreach (
            var path in new[]
            {
                ApiRoutes.Order.With(Guid.CreateVersion7()),
                ApiRoutes.Shipment.With(Guid.CreateVersion7()),
                ApiRoutes.ShipmentTracking.With(Guid.CreateVersion7()),
                ApiRoutes.OrderBilling.With(Guid.CreateVersion7()),
                ApiRoutes.Customer.With(Guid.CreateVersion7()),
                ApiRoutes.PickupPoint.With(Guid.CreateVersion7()),
            }
        )
            Assert.Equal(HttpStatusCode.NotFound, await admin.GetStatusAsync(path));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await admin.SendRawAsync(
                    HttpMethod.Put,
                    ApiRoutes.ShipmentSimulation.With(Guid.CreateVersion7()),
                    new { simulated = true }
                )
            ).StatusCode
        );
    }
}
