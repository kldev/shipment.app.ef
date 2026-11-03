using System.Net;
using Shipment.Api.Endpoints;

namespace Shipment.Tests;

/// <summary>ordering cases the first tests leave out: quantity bounds, a paused partner, the tip's rounding, picking input.</summary>
[Trait("verify", "ordering")]
[Collection(ApiCollection.Name)]
public sealed class OrderingGapTests(ApiFixture api)
{
    private static readonly HttpMethod Post = HttpMethod.Post;

    private static object Picks(params (int LineNo, int Picked)[] lines) =>
        lines
            .Select(l => new
            {
                lineNo = l.LineNo,
                pickedQuantity = l.Picked,
                takeOffCatalog = false,
            })
            .ToArray();

    [Fact]
    public async Task A_quantity_is_1_to_99()
    {
        var shop = await GapShop.CreateAsync(api);
        var bread = await shop.ProductAsync("BREAD", 899);
        var placed = await shop.Admin.PostAsync(
            ApiRoutes.Orders,
            shop.OrderBody(Guid.CreateVersion7(), 0, (bread, 99))
        );
        Assert.StartsWith("ORD-", placed["code"]!.GetValue<string>());
        foreach (var quantity in new[] { 100, -1 })
            await shop.Admin.RefusedAsync(
                Post,
                ApiRoutes.Orders,
                shop.OrderBody(Guid.CreateVersion7(), 0, (bread, quantity)),
                HttpStatusCode.BadRequest,
                "a quantity from 1 to 99"
            );
    }

    [Fact]
    public async Task An_order_is_created_with_201_and_its_location()
    {
        var shop = await GapShop.CreateAsync(api);
        var bread = await shop.ProductAsync("BREAD", 899);
        var id = Guid.CreateVersion7();
        var response = await shop.Admin.SendRawAsync(
            Post,
            ApiRoutes.Orders,
            shop.OrderBody(id, 0, (bread, 1))
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(ApiRoutes.Order.With(id), response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_paused_partner_takes_no_orders()
    {
        var shop = await GapShop.CreateAsync(api);
        var bread = await shop.ProductAsync("BREAD", 899);
        var partner = await shop.PartnerAsync();
        await shop.Admin.PutAsync(
            ApiRoutes.Partner.With(shop.PartnerId),
            new
            {
                code = shop.PartnerCode,
                name = "Gap Test Grocer",
                kind = "grocery",
                commissionBps = 1500,
                status = "paused",
                rowVersion = partner.RowVersion(),
            }
        );
        await shop.Admin.RefusedAsync(
            Post,
            ApiRoutes.Orders,
            shop.OrderBody(Guid.CreateVersion7(), 0, (bread, 1)),
            HttpStatusCode.BadRequest,
            "takes no orders now"
        );
    }

    [Theory]
    [InlineData(905L, 10, 91L)] // 90.5 -> 91
    [InlineData(50L, 5, 3L)] // 2.5 -> 3
    [InlineData(899L, 5, 45L)] // 44.95 -> 45
    public async Task The_tip_is_rounded_half_away_from_zero(long price, int tipPercent, long tip)
    {
        var shop = await GapShop.CreateAsync(api);
        var product = await shop.ProductAsync("THING", price);
        var id = Guid.CreateVersion7();
        await shop.Admin.PostAsync(ApiRoutes.Orders, shop.OrderBody(id, tipPercent, (product, 1)));
        var order = await shop.Admin.GetObjectAsync(ApiRoutes.Order.With(id));
        Assert.Equal(tip, order["tipMinor"]!.GetValue<long>());
        Assert.Equal(price + 899 + tip, order["totalMinor"]!.GetValue<long>());
    }

    [Fact]
    public async Task Picking_input_is_every_line_once_within_its_quantity_and_with_the_current_version()
    {
        var shop = await GapShop.CreateAsync(api);
        var bread = await shop.ProductAsync("BREAD", 899);
        var milk = await shop.ProductAsync("MILK", 389);
        var id = Guid.CreateVersion7();
        var placed = await shop.Admin.PostAsync(
            ApiRoutes.Orders,
            shop.OrderBody(id, 0, (bread, 2), (milk, 1))
        );
        var started = await shop.Admin.PostAsync(
            ApiRoutes.OrderPickingStart.With(id),
            new { rowVersion = placed.RowVersion() }
        );
        var finish = ApiRoutes.OrderPickingFinish.With(id);
        var cases = new (object Lines, string Message)[]
        {
            (Picks((1, 2), (1, 1)), "Decide every line of the order once"),
            (Picks((1, 2), (2, 1), (3, 1)), "Decide every line of the order once"),
            (Picks((1, 2), (9, 1)), "Decide every line of the order once"),
            (Picks((1, -1), (2, 1)), "Packed quantity is between 0 and the ordered quantity"),
        };
        foreach (var (lines, message) in cases)
            await shop.Admin.RefusedAsync(
                Post,
                finish,
                new { lines, rowVersion = started.RowVersion() },
                HttpStatusCode.BadRequest,
                message
            );
        await shop.Admin.RefusedAsync(
            Post,
            finish,
            new { lines = Picks((1, 2), (2, 1)), rowVersion = placed.RowVersion() },
            HttpStatusCode.Conflict,
            "was changed by someone else"
        );
        // Still picking after the refusals; nothing was charged or announced.
        Assert.Equal(
            "picking",
            (await shop.Admin.GetObjectAsync(ApiRoutes.Order.With(id)))[
                "status"
            ]!.GetValue<string>()
        );
        Assert.Equal(
            0L,
            await api.ScalarAsync<long>(
                $"SELECT count(*) FROM messaging.outbox_messages WHERE message_type = 'order-picked' AND payload->>'orderId' = '{id}'"
            )
        );
        var finished = await shop.Admin.PostAsync(
            finish,
            new { lines = Picks((2, 1), (1, 2)), rowVersion = started.RowVersion() }
        );
        Assert.Equal("ready", finished["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_ready_order_cannot_be_cancelled_or_picked_again()
    {
        var shop = await GapShop.CreateAsync(api);
        var bread = await shop.ProductAsync("BREAD", 899);
        var id = await DispatchTests.PackedOrderAsync(shop.Dispatch, (bread, 1));
        var ready = await shop.Admin.GetObjectAsync(ApiRoutes.Order.With(id));
        Assert.Equal("ready", ready["status"]!.GetValue<string>());
        await shop.Admin.RefusedAsync(
            Post,
            ApiRoutes.OrderCancel.With(id),
            new { reason = "Too late", rowVersion = ready.RowVersion() },
            HttpStatusCode.Conflict,
            "has not started packing can be cancelled"
        );
        await shop.Admin.RefusedAsync(
            Post,
            ApiRoutes.OrderPickingStart.With(id),
            new { rowVersion = ready.RowVersion() },
            HttpStatusCode.Conflict,
            "not waiting to be packed"
        );
    }

    [Fact]
    public async Task Orders_filter_by_status_by_its_wire_name_and_an_unknown_one_is_a_400()
    {
        var shop = await GapShop.CreateAsync(api);
        var bread = await shop.ProductAsync("BREAD", 899);
        var id = Guid.CreateVersion7();
        var placed = await shop.Admin.PostAsync(
            ApiRoutes.Orders,
            shop.OrderBody(id, 0, (bread, 1))
        );
        await shop.Admin.PostAsync(
            ApiRoutes.OrderPickingStart.With(id),
            new { rowVersion = placed.RowVersion() }
        );
        var picking = await shop.Admin.ListAsync(
            $"{ApiRoutes.Orders}?status=picking&customerId={shop.CustomerId}"
        );
        Assert.Equal(id, Assert.Single(picking)!["id"]!.GetValue<Guid>());
        Assert.Empty(
            await shop.Admin.ListAsync(
                $"{ApiRoutes.Orders}?status=placed&customerId={shop.CustomerId}"
            )
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            await shop.Admin.GetStatusAsync($"{ApiRoutes.Orders}?status=nope")
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            await shop.Admin.GetStatusAsync($"{ApiRoutes.Orders}?status=1")
        );
    }
}
