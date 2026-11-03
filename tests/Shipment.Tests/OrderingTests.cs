using System.Net;
using System.Text.Json.Nodes;
using Shipment.Api.Endpoints;

namespace Shipment.Tests;

/// <summary>Module ordering: placing an order, the delivery fee, picking with missing stock, the events (spec 003).</summary>
[Trait("verify", "ordering")]
[Collection(ApiCollection.Name)]
public sealed class OrderingTests(ApiFixture api)
{
    private static readonly HttpMethod Post = HttpMethod.Post;

    /// <summary>A partner of its own (picking may take its products off the catalog), a point, a customer.</summary>
    private sealed record Shop(
        HttpClient Admin,
        Guid PartnerId,
        Guid PointId,
        Guid CustomerId,
        Guid AddressId,
        Guid Bread,
        Guid Milk,
        Guid Wine,
        Guid Hidden
    );

    private async Task<Shop> ShopAsync()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var partnerId = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.Partners,
            new
            {
                id = partnerId,
                code = $"O{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
                name = "Order Test Grocer",
                kind = "grocery",
                commissionBps = 1500,
            }
        );
        var pointId = Guid.CreateVersion7();
        await admin.PostAsync(ApiRoutes.PickupPoints, PointBody(pointId, partnerId));
        async Task<Guid> Product(string sku, string name, long price, bool ageRestricted = false)
        {
            var id = Guid.CreateVersion7();
            await admin.PostAsync(
                ApiRoutes.Products,
                new
                {
                    id,
                    partnerId,
                    sku,
                    name,
                    category = "groceries",
                    priceMinor = price,
                    weightG = 600,
                    sizeClass = "S",
                    ageRestricted,
                }
            );
            return id;
        }
        var bread = await Product("BREAD", "Rye bread", 899);
        var milk = await Product("MILK", "Milk 1 l", 389);
        var wine = await Product("WINE", "Red wine", 3999, ageRestricted: true);
        var hidden = await Product("HIDDEN", "Out of season", 100);
        var hiddenVersion = (
            await CatalogTests.ReadProductAsync(admin, partnerId, hidden)
        ).RowVersion();
        await admin.PutAsync(
            ApiRoutes.ProductListed.With(hidden),
            new { listedAvailable = false, rowVersion = hiddenVersion }
        );

        var customerId = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.Customers,
            new
            {
                id = customerId,
                displayName = "Order Tester",
                email = $"{customerId:N}@example.com",
            }
        );
        var addressId = Guid.CreateVersion7();
        // About 2.3 km from the point: the second fee band (8.99).
        await admin.PostAsync(
            ApiRoutes.CustomerAddresses.With(customerId),
            new
            {
                id = addressId,
                label = "Home",
                addressLine = "ul. Długa 15",
                city = "Kraków",
                postcode = "31-147",
                latitude = 50.0706m,
                longitude = 19.9377m,
            }
        );
        return new Shop(
            admin,
            partnerId,
            pointId,
            customerId,
            addressId,
            bread,
            milk,
            wine,
            hidden
        );
    }

    private static object PointBody(Guid id, Guid partnerId) =>
        new
        {
            id,
            partnerId,
            name = "Kazimierz",
            addressLine = "ul. Miodowa 22",
            city = "Kraków",
            postcode = "31-055",
            latitude = 50.0513m,
            longitude = 19.9446m,
            opensMinute = 0,
            closesMinute = 1440,
        };

    private static object Order(
        Guid id,
        Shop shop,
        params (Guid ProductId, int Quantity)[] lines
    ) =>
        new
        {
            id,
            customerId = shop.CustomerId,
            addressId = shop.AddressId,
            pickupPointId = shop.PointId,
            lines = lines
                .Select(l => new { productId = l.ProductId, quantity = l.Quantity })
                .ToArray(),
        };

    private static object Picks(params (int LineNo, int Picked, bool TakeOff)[] lines) =>
        lines
            .Select(l => new
            {
                lineNo = l.LineNo,
                pickedQuantity = l.Picked,
                takeOffCatalog = l.TakeOff,
            })
            .ToArray();

    private static string OrderRoute(Guid id) => ApiRoutes.Order.With(id);

    private async Task<JsonObject> OutboxAsync(string type, Guid orderId)
    {
        var payload = await api.ScalarAsync<string>(
            $"SELECT payload::text FROM messaging.outbox_messages WHERE message_type = '{type}' AND payload->>'orderId' = '{orderId}'"
        );
        return JsonNode.Parse(payload)!.AsObject();
    }

    private async Task<long> OutboxCountAsync(string type, Guid orderId) =>
        await api.ScalarAsync<long>(
            $"SELECT count(*) FROM messaging.outbox_messages WHERE message_type = '{type}' AND payload->>'orderId' = '{orderId}'"
        );

    [Fact]
    public async Task An_order_copies_prices_and_places_once_with_the_fee_of_its_distance()
    {
        var shop = await ShopAsync();
        var id = Guid.CreateVersion7();

        var quote = await shop.Admin.GetObjectAsync(
            $"{ApiRoutes.Quote}?pickupPointId={shop.PointId}&addressId={shop.AddressId}"
        );
        Assert.InRange(quote["distanceM"]!.GetValue<int>(), 2001, 5000);
        Assert.Equal(899, quote["deliveryFeeMinor"]!.GetValue<long>());
        Assert.True(quote["deliverable"]!.GetValue<bool>());
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await shop.Admin.GetAsync(
                    $"{ApiRoutes.Quote}?pickupPointId={Guid.CreateVersion7()}&addressId={shop.AddressId}",
                    TestContext.Current.CancellationToken
                )
            ).StatusCode
        );

        var placed = await shop.Admin.PostAsync(
            ApiRoutes.Orders,
            Order(id, shop, (shop.Bread, 2), (shop.Milk, 3))
        );
        Assert.StartsWith("ORD-", placed["code"]!.GetValue<string>());
        // A retried request: the same order back, nothing new.
        var again = await shop.Admin.PostAsync(
            ApiRoutes.Orders,
            Order(id, shop, (shop.Milk, 3), (shop.Bread, 2))
        );
        Assert.Equal(placed["code"]!.GetValue<string>(), again["code"]!.GetValue<string>());
        Assert.Equal(placed.RowVersion(), again.RowVersion());
        Assert.Equal(1, await OutboxCountAsync("order-placed", id));
        await shop.Admin.RefusedAsync(
            Post,
            ApiRoutes.Orders,
            Order(id, shop, (shop.Bread, 1)),
            HttpStatusCode.Conflict,
            "already used by another order"
        );

        var order = await shop.Admin.GetObjectAsync(OrderRoute(id));
        Assert.Equal("placed", order["status"]!.GetValue<string>());
        Assert.Equal(2 * 899 + 3 * 389, order["goodsOrderedMinor"]!.GetValue<long>());
        Assert.Equal(2 * 899 + 3 * 389 + 899, order["totalMinor"]!.GetValue<long>());
        Assert.Equal("Kazimierz", order["pickupPointName"]!.GetValue<string>());
        Assert.False(order["ageRestricted"]!.GetValue<bool>());
        Assert.Contains(
            await shop.Admin.ListAsync(
                $"{ApiRoutes.Orders}?customerId={shop.CustomerId}&status=placed"
            ),
            o => o!["id"]!.GetValue<Guid>() == id
        );

        // The catalog's price changes later; the order keeps its own.
        var bread = await CatalogTests.ReadProductAsync(shop.Admin, shop.PartnerId, shop.Bread);
        await shop.Admin.PutAsync(
            ApiRoutes.Product.With(shop.Bread),
            new
            {
                sku = "BREAD",
                name = "Rye bread",
                category = "groceries",
                priceMinor = 1999,
                weightG = 600,
                sizeClass = "S",
                ageRestricted = false,
                listedAvailable = true,
                rowVersion = bread.RowVersion(),
            }
        );
        var lines = await shop.Admin.ListAsync(ApiRoutes.OrderLines.With(id));
        Assert.Equal(
            899,
            lines.Single(l => l!["sku"]!.GetValue<string>() == "BREAD")![
                "unitPriceMinor"
            ]!.GetValue<long>()
        );
    }

    [Fact]
    public async Task Placing_rules_are_refused_with_their_messages()
    {
        var shop = await ShopAsync();
        var other = await ShopAsync();
        var admin = shop.Admin;
        async Task Refused(object body, HttpStatusCode status, string message) =>
            await admin.RefusedAsync(Post, ApiRoutes.Orders, body, status, message);
        object Body(Guid customerId, Guid addressId, Guid pointId, int tipPercent = 0) =>
            new
            {
                id = Guid.CreateVersion7(),
                customerId,
                addressId,
                pickupPointId = pointId,
                lines = new[] { new { productId = shop.Bread, quantity = 1 } },
                tipPercent,
            };

        await Refused(
            Order(Guid.CreateVersion7(), shop),
            HttpStatusCode.BadRequest,
            "Add at least one product"
        );
        await Refused(
            Order(Guid.CreateVersion7(), shop, (shop.Bread, 0)),
            HttpStatusCode.BadRequest,
            "a quantity from 1 to 99"
        );
        await Refused(
            Order(Guid.CreateVersion7(), shop, (shop.Bread, 1), (shop.Bread, 2)),
            HttpStatusCode.BadRequest,
            "Each product only once"
        );
        await Refused(
            Order(Guid.CreateVersion7(), shop, (shop.Hidden, 1)),
            HttpStatusCode.BadRequest,
            "Not available from this partner: Out of season."
        );
        await Refused(
            Order(Guid.CreateVersion7(), shop, (other.Bread, 1)),
            HttpStatusCode.BadRequest,
            "Not available from this partner"
        );
        await Refused(
            Order(Guid.Empty, shop, (shop.Bread, 1)),
            HttpStatusCode.BadRequest,
            "Order id is required"
        );
        await Refused(
            Body(shop.CustomerId, shop.AddressId, shop.PointId, tipPercent: 7),
            HttpStatusCode.BadRequest,
            "The tip is 0, 5 or 10 percent"
        );
        await Refused(
            Body(shop.CustomerId, other.AddressId, shop.PointId),
            HttpStatusCode.NotFound,
            "not one of the customer's"
        );
        await Refused(
            Body(Guid.CreateVersion7(), shop.AddressId, shop.PointId),
            HttpStatusCode.NotFound,
            "Customer not found"
        );
        await Refused(
            Body(shop.CustomerId, shop.AddressId, Guid.CreateVersion7()),
            HttpStatusCode.NotFound,
            "Pickup point not found"
        );

        // Warsaw is beyond the last band.
        var far = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.CustomerAddresses.With(shop.CustomerId),
            new
            {
                id = far,
                label = "Warsaw",
                addressLine = "Marszałkowska 1",
                city = "Warszawa",
                postcode = "00-001",
                latitude = 52.2297m,
                longitude = 21.0122m,
            }
        );
        var quote = await admin.GetObjectAsync(
            $"{ApiRoutes.Quote}?pickupPointId={shop.PointId}&addressId={far}"
        );
        Assert.False(quote["deliverable"]!.GetValue<bool>());
        Assert.Null(quote["deliveryFeeMinor"]);
        await Refused(
            Body(shop.CustomerId, far, shop.PointId),
            HttpStatusCode.BadRequest,
            "too far from the pickup point"
        );

        // A closed point takes no orders.
        var point = await admin.GetObjectAsync(ApiRoutes.PickupPoint.With(shop.PointId));
        await admin.PutAsync(
            ApiRoutes.PickupPoint.With(shop.PointId),
            new
            {
                name = "Kazimierz",
                addressLine = "ul. Miodowa 22",
                city = "Kraków",
                postcode = "31-055",
                latitude = 50.0513m,
                longitude = 19.9446m,
                opensMinute = 0,
                closesMinute = 1440,
                status = "closed",
                rowVersion = point.RowVersion(),
            }
        );
        await Refused(
            Order(Guid.CreateVersion7(), shop, (shop.Bread, 1)),
            HttpStatusCode.BadRequest,
            "takes no orders now"
        );
    }

    [Fact]
    public async Task Picking_with_a_missing_line_charges_only_what_was_packed_and_says_what_is_missing()
    {
        var shop = await ShopAsync();
        var admin = shop.Admin;
        var id = Guid.CreateVersion7();
        var finish = ApiRoutes.OrderPickingFinish.With(id);
        var placed = await admin.PostAsync(
            ApiRoutes.Orders,
            Order(id, shop, (shop.Bread, 2), (shop.Milk, 3), (shop.Wine, 1))
        );
        Assert.True(
            (await admin.GetObjectAsync(OrderRoute(id)))["ageRestricted"]!.GetValue<bool>()
        );

        var lines = Picks((1, 2, false), (2, 1, false), (3, 0, true));
        await admin.RefusedAsync(
            Post,
            finish,
            new { lines, rowVersion = placed.RowVersion() },
            HttpStatusCode.Conflict,
            "Start picking the order first"
        );
        var started = await admin.PostAsync(
            ApiRoutes.OrderPickingStart.With(id),
            new { rowVersion = placed.RowVersion() }
        );
        Assert.NotEqual(placed.RowVersion(), started.RowVersion());
        // Already picking: a no-op.
        Assert.Equal(
            started.RowVersion(),
            (
                await admin.PostAsync(
                    ApiRoutes.OrderPickingStart.With(id),
                    new { rowVersion = started.RowVersion() }
                )
            ).RowVersion()
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.OrderPickingStart.With(id),
            new { rowVersion = placed.RowVersion() },
            HttpStatusCode.Conflict,
            "was changed by someone else"
        );
        Assert.Contains(
            await admin.ListAsync($"{ApiRoutes.PickingQueue}?pickupPointId={shop.PointId}"),
            q => q!["id"]!.GetValue<Guid>() == id
        );

        await admin.RefusedAsync(
            Post,
            finish,
            new { lines = Picks((1, 2, false), (2, 1, false)), rowVersion = started.RowVersion() },
            HttpStatusCode.BadRequest,
            "Decide every line of the order once"
        );
        await admin.RefusedAsync(
            Post,
            finish,
            new
            {
                lines = Picks((1, 2, false), (2, 4, false), (3, 0, true)),
                rowVersion = started.RowVersion(),
            },
            HttpStatusCode.BadRequest,
            "Packed quantity is between 0 and the ordered quantity"
        );

        var finished = await admin.PostAsync(
            finish,
            new { lines, rowVersion = started.RowVersion() }
        );
        Assert.Equal("ready", finished["status"]!.GetValue<string>());

        var order = await admin.GetObjectAsync(OrderRoute(id));
        Assert.Equal(2 * 899 + 1 * 389, order["goodsPickedMinor"]!.GetValue<long>());
        Assert.Equal(2 * 899 + 1 * 389 + 899, order["totalMinor"]!.GetValue<long>());
        Assert.Equal(2, order["missingLines"]!.GetValue<int>());
        // The wine was missing: no ID check needed for what is left.
        Assert.False(order["ageRestricted"]!.GetValue<bool>());
        Assert.Empty(
            await admin.ListAsync($"{ApiRoutes.PickingQueue}?pickupPointId={shop.PointId}")
        );

        var picked = await OutboxAsync("order-picked", id);
        Assert.Equal(
            [(1, 2), (2, 1)],
            picked["pickedLines"]!
                .AsArray()
                .Select(l => (l!["lineNo"]!.GetValue<int>(), l["quantity"]!.GetValue<int>()))
        );
        Assert.Equal(
            [(2, 2), (3, 1)],
            picked["missingLines"]!
                .AsArray()
                .Select(l => (l!["lineNo"]!.GetValue<int>(), l["quantity"]!.GetValue<int>()))
        );
        // "Take it off the catalog": the wine is no longer listed.
        Assert.False(
            (await CatalogTests.ReadProductAsync(admin, shop.PartnerId, shop.Wine))[
                "listedAvailable"
            ]!.GetValue<bool>()
        );

        await admin.RefusedAsync(
            Post,
            finish,
            new { lines, rowVersion = finished.RowVersion() },
            HttpStatusCode.Conflict,
            "Start picking the order first"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.OrderPickingFinish.With(Guid.CreateVersion7()),
            new { lines, rowVersion = 1 },
            HttpStatusCode.NotFound,
            "Order not found"
        );
    }

    [Fact]
    public async Task Nothing_on_the_shelf_cancels_the_order_and_a_placed_order_can_be_cancelled()
    {
        var shop = await ShopAsync();
        var admin = shop.Admin;

        var empty = Guid.CreateVersion7();
        var placed = await admin.PostAsync(ApiRoutes.Orders, Order(empty, shop, (shop.Bread, 1)));
        var started = await admin.PostAsync(
            ApiRoutes.OrderPickingStart.With(empty),
            new { rowVersion = placed.RowVersion() }
        );
        var finished = await admin.PostAsync(
            ApiRoutes.OrderPickingFinish.With(empty),
            new { lines = Picks((1, 0, false)), rowVersion = started.RowVersion() }
        );
        Assert.Equal("cancelled", finished["status"]!.GetValue<string>());
        Assert.Equal(0, await OutboxCountAsync("order-picked", empty));
        Assert.Equal(
            "None of the products was available.",
            (await OutboxAsync("order-cancelled", empty))["reason"]!.GetValue<string>()
        );

        var id = Guid.CreateVersion7();
        var cancel = ApiRoutes.OrderCancel.With(id);
        var order = await admin.PostAsync(ApiRoutes.Orders, Order(id, shop, (shop.Milk, 1)));
        await admin.RefusedAsync(
            Post,
            cancel,
            new { reason = " ", rowVersion = order.RowVersion() },
            HttpStatusCode.BadRequest,
            "Say why the order is cancelled"
        );
        await admin.RefusedAsync(
            Post,
            cancel,
            new { reason = "Customer changed their mind", rowVersion = order.RowVersion() + 1 },
            HttpStatusCode.Conflict,
            "was changed by someone else"
        );
        await admin.PostAsync(
            cancel,
            new { reason = "Customer changed their mind", rowVersion = order.RowVersion() }
        );
        var cancelled = await admin.GetObjectAsync(OrderRoute(id));
        Assert.Equal("cancelled", cancelled["status"]!.GetValue<string>());
        Assert.Equal("Customer changed their mind", cancelled["cancelReason"]!.GetValue<string>());
        Assert.Equal(1, await OutboxCountAsync("order-cancelled", id));

        var packing = Guid.CreateVersion7();
        var second = await admin.PostAsync(ApiRoutes.Orders, Order(packing, shop, (shop.Milk, 1)));
        var picking = await admin.PostAsync(
            ApiRoutes.OrderPickingStart.With(packing),
            new { rowVersion = second.RowVersion() }
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.OrderCancel.With(packing),
            new { reason = "Too late", rowVersion = picking.RowVersion() },
            HttpStatusCode.Conflict,
            "has not started packing can be cancelled"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.OrderPickingStart.With(id),
            new { rowVersion = cancelled.RowVersion() },
            HttpStatusCode.Conflict,
            "not waiting to be packed"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.OrderCancel.With(Guid.CreateVersion7()),
            new { reason = "x", rowVersion = 1 },
            HttpStatusCode.NotFound,
            "Order not found"
        );
    }

    [Fact]
    public async Task Picking_history_shows_finished_pickings_with_who_packed_and_what_was_missing()
    {
        var shop = await ShopAsync();
        var admin = shop.Admin;

        async Task PickAsync(Guid id, params int[] picked)
        {
            var started = await admin.PostAsync(
                ApiRoutes.OrderPickingStart.With(id),
                new { rowVersion = (await admin.GetObjectAsync(OrderRoute(id))).RowVersion() }
            );
            await admin.PostAsync(
                ApiRoutes.OrderPickingFinish.With(id),
                new
                {
                    lines = Picks(picked.Select((q, i) => (i + 1, q, false)).ToArray()),
                    rowVersion = started.RowVersion(),
                }
            );
        }

        var partial = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.Orders,
            Order(partial, shop, (shop.Bread, 2), (shop.Milk, 3))
        );
        await PickAsync(partial, 2, 1);
        var full = Guid.CreateVersion7();
        await admin.PostAsync(ApiRoutes.Orders, Order(full, shop, (shop.Milk, 1)));
        await PickAsync(full, 1);
        var empty = Guid.CreateVersion7();
        await admin.PostAsync(ApiRoutes.Orders, Order(empty, shop, (shop.Bread, 1)));
        await PickAsync(empty, 0);
        var waiting = Guid.CreateVersion7();
        await admin.PostAsync(ApiRoutes.Orders, Order(waiting, shop, (shop.Milk, 1)));
        var dropped = Guid.CreateVersion7();
        var placed = await admin.PostAsync(ApiRoutes.Orders, Order(dropped, shop, (shop.Milk, 1)));
        await admin.PostAsync(
            ApiRoutes.OrderCancel.With(dropped),
            new { reason = "Customer changed their mind", rowVersion = placed.RowVersion() }
        );

        var page = await admin.GetObjectAsync(
            $"{ApiRoutes.PickingHistory}?pickupPointId={shop.PointId}"
        );
        Assert.False(page["hasMore"]!.GetValue<bool>());
        var history = page["items"]!.AsArray();
        // Newest first; not picked yet or cancelled before picking: not in the history.
        Assert.Equal([empty, full, partial], history.Select(h => h!["id"]!.GetValue<Guid>()));

        var row = history.Single(h => h!["id"]!.GetValue<Guid>() == partial)!;
        Assert.Equal("ready", row["status"]!.GetValue<string>());
        Assert.Equal(ApiFixture.AdminId, row["pickedBy"]!.GetValue<Guid>());
        Assert.False(string.IsNullOrEmpty(row["pickedByName"]!.GetValue<string>()));
        Assert.Equal(2, row["lineCount"]!.GetValue<int>());
        Assert.Equal(5, row["itemCount"]!.GetValue<int>());
        Assert.Equal(3, row["itemsPicked"]!.GetValue<int>());
        Assert.Equal(2, row["itemsMissing"]!.GetValue<int>());
        Assert.Equal(1, row["missingLineCount"]!.GetValue<int>());
        Assert.True(row["hasMissing"]!.GetValue<bool>());
        Assert.Equal(2 * 899 + 3 * 389, row["goodsOrderedMinor"]!.GetValue<long>());
        Assert.Equal(2 * 899 + 1 * 389, row["goodsPickedMinor"]!.GetValue<long>());
        Assert.True(row["pickingMinutes"]!.GetValue<int>() >= 0);

        var cancelled = history.Single(h => h!["id"]!.GetValue<Guid>() == empty)!;
        Assert.Equal("cancelled", cancelled["status"]!.GetValue<string>());
        Assert.Equal(1, cancelled["itemsMissing"]!.GetValue<int>());

        Assert.Equal(
            [empty, partial],
            (
                await admin.GetObjectAsync(
                    $"{ApiRoutes.PickingHistory}?pickupPointId={shop.PointId}&hasMissing=true"
                )
            )["items"]!
                .AsArray()
                .Select(h => h!["id"]!.GetValue<Guid>())
        );
        // One per page: the newest, and more follow.
        var first = await admin.GetObjectAsync(
            $"{ApiRoutes.PickingHistory}?pickupPointId={shop.PointId}&take=1"
        );
        Assert.Equal(empty, Assert.Single(first["items"]!.AsArray())!["id"]!.GetValue<Guid>());
        Assert.True(first["hasMore"]!.GetValue<bool>());
        var tomorrow = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        Assert.Empty(
            (
                await admin.GetObjectAsync(
                    $"{ApiRoutes.PickingHistory}?pickupPointId={shop.PointId}&from={tomorrow}"
                )
            )["items"]!.AsArray()
        );
    }
}
