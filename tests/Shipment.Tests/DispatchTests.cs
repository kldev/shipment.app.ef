using System.Net;
using System.Text.Json.Nodes;
using Shipment.Api.Endpoints;
using Shipment.Application.Dispatch;
using Shipment.Application.Ordering;
using Shipment.Domain.Common;

namespace Shipment.Tests;

/// <summary>
/// Module dispatch: the shipment the runner creates from a packed order, the courier proposals and the assignment,
/// the lifecycle, and ordering's reaction to its end (spec 004). The runner's part is the application service called
/// without a user, the way its handlers call it.
/// </summary>
[Trait("verify", "dispatch")]
[Collection(ApiCollection.Name)]
public sealed class DispatchTests(ApiFixture api)
{
    private static readonly HttpMethod Post = HttpMethod.Post;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A partner with a point in Kazimierz, light and heavy products and a customer about 2.3 km away.</summary>
    internal sealed record Shop(
        HttpClient Admin,
        Guid PointId,
        Guid CustomerId,
        Guid AddressId,
        Guid Bread,
        Guid Wine,
        Guid Crate
    );

    internal static async Task<Shop> ShopAsync(ApiFixture api)
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var partnerId = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.Partners,
            new
            {
                id = partnerId,
                code = $"D{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
                name = "Dispatch Test Grocer",
                kind = "grocery",
                commissionBps = 1500,
            }
        );
        var pointId = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.PickupPoints,
            new
            {
                id = pointId,
                partnerId,
                name = "Kazimierz",
                addressLine = "ul. Miodowa 22",
                city = "Kraków",
                postcode = "31-055",
                latitude = 50.0513m,
                longitude = 19.9446m,
                opensMinute = 0,
                closesMinute = 1440,
            }
        );
        async Task<Guid> Product(
            string sku,
            int weightG,
            string sizeClass,
            bool ageRestricted = false
        )
        {
            var id = Guid.CreateVersion7();
            await admin.PostAsync(
                ApiRoutes.Products,
                new
                {
                    id,
                    partnerId,
                    sku,
                    name = sku,
                    category = "groceries",
                    priceMinor = 500,
                    weightG,
                    sizeClass,
                    ageRestricted,
                }
            );
            return id;
        }
        var bread = await Product("BREAD", 600, "S");
        var wine = await Product("WINE", 1300, "S", ageRestricted: true);
        var crate = await Product("CRATE", 9000, "M");

        var customerId = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.Customers,
            new
            {
                id = customerId,
                displayName = "Dispatch Tester",
                email = $"{customerId:N}@example.com",
            }
        );
        var addressId = Guid.CreateVersion7();
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
        return new Shop(admin, pointId, customerId, addressId, bread, wine, crate);
    }

    /// <summary>An order placed for the shop's customer; its id and the answer (code, rowVersion).</summary>
    internal static async Task<(Guid Id, JsonObject Placed)> PlaceAsync(
        Shop shop,
        (Guid ProductId, int Quantity)[] lines,
        int tipPercent = 0
    )
    {
        var id = Guid.CreateVersion7();
        var placed = await shop.Admin.PostAsync(
            ApiRoutes.Orders,
            new
            {
                id,
                customerId = shop.CustomerId,
                addressId = shop.AddressId,
                pickupPointId = shop.PointId,
                lines = lines
                    .Select(l => new { productId = l.ProductId, quantity = l.Quantity })
                    .ToArray(),
                tipPercent,
            }
        );
        return (id, placed);
    }

    /// <summary>An order placed and packed with the given packed quantities per line (in order of the lines).</summary>
    internal static async Task<Guid> PackedAsync(
        Shop shop,
        (Guid ProductId, int Quantity)[] lines,
        int[] packed,
        int tipPercent = 0
    )
    {
        var (id, placed) = await PlaceAsync(shop, lines, tipPercent);
        var started = await shop.Admin.PostAsync(
            ApiRoutes.OrderPickingStart.With(id),
            new { rowVersion = placed.RowVersion() }
        );
        var orderLines = (await shop.Admin.ListAsync(ApiRoutes.OrderLines.With(id)))
            .OrderBy(l => l!["lineNo"]!.GetValue<int>())
            .ToList();
        await shop.Admin.PostAsync(
            ApiRoutes.OrderPickingFinish.With(id),
            new
            {
                lines = orderLines
                    .Select(
                        (l, i) =>
                            new
                            {
                                lineNo = l!["lineNo"]!.GetValue<int>(),
                                pickedQuantity = packed[i],
                                takeOffCatalog = false,
                            }
                    )
                    .ToArray(),
                rowVersion = started.RowVersion(),
            }
        );
        return id;
    }

    /// <summary>An order placed and packed in full; returns its id.</summary>
    internal static Task<Guid> PackedOrderAsync(
        Shop shop,
        params (Guid ProductId, int Quantity)[] lines
    ) => PackedAsync(shop, lines, lines.Select(l => l.Quantity).ToArray());

    /// <summary>What the runner does on order-picked; returns the shipment.</summary>
    internal static async Task<JsonObject> ShipmentAsync(
        ApiFixture api,
        HttpClient admin,
        Guid orderId
    )
    {
        var created = await api.RunnerAsync<DispatchService, Result>(s =>
            s.CreateForOrderAsync(orderId, Ct)
        );
        Assert.True(created.IsSuccess, created.Error?.Message);
        return Assert
            .Single(await admin.ListAsync($"{ApiRoutes.Shipments}?orderId={orderId}"))!
            .AsObject();
    }

    /// <summary>A courier on shift at a place; cleared = every document it needs, verified.</summary>
    internal static async Task<Guid> CourierAsync(
        HttpClient admin,
        string vehicleType,
        bool cleared,
        decimal latitude,
        decimal longitude
    )
    {
        var id = Guid.CreateVersion7();
        var created = await admin.PostAsync(
            ApiRoutes.Couriers,
            CarriersTests.Courier(id, vehicleType, vehicleType == "bike" ? null : "KR 1234")
        );
        var kinds =
            vehicleType == "bike"
                ? new[] { "id_card" }
                : ["id_card", "driving_licence", "vehicle_insurance"];
        var nextYear = OperatorTime.Today(DateTimeOffset.UtcNow).AddYears(1);
        foreach (var kind in cleared ? kinds : kinds[..^1])
            await ComplianceTests.VerifyAsync(
                admin,
                await ComplianceTests.DocumentAsync(admin, id, kind, nextYear)
            );
        await admin.PostAsync(
            ApiRoutes.CourierShiftStart.With(id),
            new { rowVersion = created.RowVersion() }
        );
        await admin.PutAsync(ApiRoutes.CourierPosition.With(id), new { latitude, longitude });
        return id;
    }

    /// <summary>The shipment assigned to the courier; the new row version.</summary>
    internal static async Task<uint> AssignAsync(
        HttpClient admin,
        Guid shipmentId,
        Guid courierId,
        uint rowVersion
    ) =>
        (
            await admin.PostAsync(
                ApiRoutes.ShipmentAssign.With(shipmentId),
                new { courierId, rowVersion }
            )
        ).RowVersion();

    /// <summary>The courier's next step; the new row version.</summary>
    internal static async Task<uint> AdvanceAsync(
        HttpClient admin,
        Guid shipmentId,
        uint rowVersion,
        string status,
        bool ageChecked = false
    ) =>
        (
            await admin.PostAsync(
                ApiRoutes.ShipmentAdvance.With(shipmentId),
                new
                {
                    status,
                    ageChecked,
                    rowVersion,
                }
            )
        ).RowVersion();

    private static async Task<string> ShiftAsync(HttpClient admin, Guid courierId) =>
        (await admin.GetObjectAsync(ApiRoutes.Courier.With(courierId)))[
            "shiftStatus"
        ]!.GetValue<string>();

    private async Task<int> OutboxCountAsync(string type, Guid shipmentId) =>
        (int)
            await api.ScalarAsync<long>(
                $"SELECT count(*) FROM messaging.outbox_messages WHERE message_type = '{type}' AND payload->>'shipmentId' = '{shipmentId}'"
            );

    private Task<Result> CompleteAsync(Guid orderId, bool delivered, string? reason) =>
        api.RunnerAsync<OrderingService, Result>(s =>
            s.CompleteAsync(orderId, delivered, reason, Ct)
        );

    [Fact]
    public async Task A_packed_order_gets_one_shipment_with_the_smallest_vehicle_that_carries_it()
    {
        var shop = await ShopAsync(api);

        var light = await PackedOrderAsync(shop, (shop.Bread, 2), (shop.Wine, 1));
        var shipment = await ShipmentAsync(api, shop.Admin, light);
        Assert.StartsWith("SHP-", shipment["code"]!.GetValue<string>());
        Assert.Equal("available", shipment["status"]!.GetValue<string>());
        Assert.Equal("bike", shipment["requiredVehicle"]!.GetValue<string>());
        Assert.Equal(2 * 600 + 1300, shipment["weightG"]!.GetValue<int>());
        Assert.Equal(3, shipment["itemCount"]!.GetValue<int>());
        Assert.True(shipment["ageCheckRequired"]!.GetValue<bool>());
        Assert.Equal("Kazimierz", shipment["pickupPointName"]!.GetValue<string>());
        Assert.Equal("ul. Długa 15", shipment["dropoffAddressLine"]!.GetValue<string>());
        var id = shipment["id"]!.GetValue<Guid>();
        Assert.Equal(
            id,
            (await shop.Admin.GetObjectAsync(ApiRoutes.Shipment.With(id)))["id"]!.GetValue<Guid>()
        );
        // A repeated order-picked: the same shipment, nothing new.
        var again = await ShipmentAsync(api, shop.Admin, light);
        Assert.Equal(id, again["id"]!.GetValue<Guid>());

        // Over a bike's 8 kg: a car.
        var heavy = await PackedOrderAsync(shop, (shop.Crate, 1));
        Assert.Equal(
            "car",
            (await ShipmentAsync(api, shop.Admin, heavy))["requiredVehicle"]!.GetValue<string>()
        );
        // Over a car's 40 kg: a van.
        var bulk = await PackedOrderAsync(shop, (shop.Crate, 5));
        Assert.Equal(
            "van",
            (await ShipmentAsync(api, shop.Admin, bulk))["requiredVehicle"]!.GetValue<string>()
        );

        // An order that is not ready (cancelled before packing) gets no shipment.
        var (cancelledId, placed) = await PlaceAsync(shop, [(shop.Bread, 1)]);
        await shop.Admin.PostAsync(
            ApiRoutes.OrderCancel.With(cancelledId),
            new { reason = "Changed my mind", rowVersion = placed.RowVersion() }
        );
        Assert.True(
            (
                await api.RunnerAsync<DispatchService, Result>(s =>
                    s.CreateForOrderAsync(cancelledId, Ct)
                )
            ).IsSuccess
        );
        Assert.Empty(await shop.Admin.ListAsync($"{ApiRoutes.Shipments}?orderId={cancelledId}"));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await shop.Admin.GetAsync(ApiRoutes.Shipment.With(Guid.CreateVersion7()), Ct)
            ).StatusCode
        );
    }

    [Fact]
    public async Task Proposals_are_available_cleared_couriers_on_a_big_enough_vehicle_nearest_first()
    {
        var shop = await ShopAsync(api);
        var shipment = await ShipmentAsync(
            api,
            shop.Admin,
            await PackedOrderAsync(shop, (shop.Crate, 1))
        );
        var shipmentId = shipment["id"]!.GetValue<Guid>();
        var assign = ApiRoutes.ShipmentAssign.With(shipmentId);

        // The point is at 50.0513, 19.9446.
        var nearCar = await CourierAsync(shop.Admin, "car", cleared: true, 50.0520m, 19.9450m);
        var farVan = await CourierAsync(shop.Admin, "van", cleared: true, 50.0900m, 19.9800m);
        var bike = await CourierAsync(shop.Admin, "bike", cleared: true, 50.0514m, 19.9447m);
        var uncleared = await CourierAsync(shop.Admin, "car", cleared: false, 50.0515m, 19.9446m);

        var proposals = (await shop.Admin.ListAsync(ApiRoutes.ShipmentProposals.With(shipmentId)))
            .Select(p => p!["courierId"]!.GetValue<Guid>())
            .ToList();
        Assert.Contains(nearCar, proposals);
        Assert.Contains(farVan, proposals);
        Assert.True(proposals.IndexOf(nearCar) < proposals.IndexOf(farVan));
        Assert.DoesNotContain(bike, proposals);
        Assert.DoesNotContain(uncleared, proposals);

        await shop.Admin.RefusedAsync(
            Post,
            assign,
            new { courierId = bike, rowVersion = shipment.RowVersion() },
            HttpStatusCode.Conflict,
            "The courier cannot take this shipment now"
        );
        await shop.Admin.RefusedAsync(
            Post,
            assign,
            new { courierId = Guid.CreateVersion7(), rowVersion = shipment.RowVersion() },
            HttpStatusCode.NotFound,
            "Courier not found"
        );
        await shop.Admin.RefusedAsync(
            Post,
            ApiRoutes.ShipmentAssign.With(Guid.CreateVersion7()),
            new { courierId = nearCar, rowVersion = 1 },
            HttpStatusCode.NotFound,
            "Shipment not found"
        );
        await shop.Admin.RefusedAsync(
            Post,
            assign,
            new { courierId = nearCar, rowVersion = shipment.RowVersion() + 1 },
            HttpStatusCode.Conflict,
            "was changed by someone else"
        );
        await shop.Admin.RefusedAsync(
            Post,
            assign,
            new { courierId = Guid.Empty, rowVersion = shipment.RowVersion() },
            HttpStatusCode.BadRequest,
            "Choose a courier"
        );

        var assigned = await AssignAsync(shop.Admin, shipmentId, nearCar, shipment.RowVersion());
        Assert.Equal("busy", await ShiftAsync(shop.Admin, nearCar));
        Assert.Equal(1, await OutboxCountAsync("courier-assigned", shipmentId));
        Assert.Empty(await shop.Admin.ListAsync(ApiRoutes.ShipmentProposals.With(shipmentId)));
        await shop.Admin.RefusedAsync(
            Post,
            assign,
            new { courierId = farVan, rowVersion = assigned },
            HttpStatusCode.Conflict,
            "already has a courier"
        );

        // The race: the same courier for a second shipment - no longer available.
        var other = await ShipmentAsync(
            api,
            shop.Admin,
            await PackedOrderAsync(shop, (shop.Crate, 1))
        );
        await shop.Admin.RefusedAsync(
            Post,
            ApiRoutes.ShipmentAssign.With(other["id"]!.GetValue<Guid>()),
            new { courierId = nearCar, rowVersion = other.RowVersion() },
            HttpStatusCode.Conflict,
            "The courier cannot take this shipment now"
        );
        // Busy is dispatch's: the API has no call that makes a courier busy, so only the assignment above can.
    }

    [Fact]
    public async Task A_shipment_goes_step_by_step_to_delivered_with_the_id_check_and_the_order_follows()
    {
        var shop = await ShopAsync(api);
        var orderId = await PackedOrderAsync(shop, (shop.Bread, 1), (shop.Wine, 1));
        var shipment = await ShipmentAsync(api, shop.Admin, orderId);
        var id = shipment["id"]!.GetValue<Guid>();
        var advance = ApiRoutes.ShipmentAdvance.With(id);

        await shop.Admin.RefusedAsync(
            Post,
            advance,
            new { status = "arrived_at_pickup", rowVersion = shipment.RowVersion() },
            HttpStatusCode.Conflict,
            "Assign a courier first"
        );
        var courier = await CourierAsync(shop.Admin, "bike", cleared: true, 50.0514m, 19.9447m);
        var version = await AssignAsync(shop.Admin, id, courier, shipment.RowVersion());
        await shop.Admin.RefusedAsync(
            Post,
            advance,
            new { status = "picked_up", rowVersion = version },
            HttpStatusCode.Conflict,
            "The next step is arrived at pickup"
        );
        await shop.Admin.NotBoundAsync(
            Post,
            advance,
            new { status = "teleported", rowVersion = version }
        );
        await shop.Admin.RefusedAsync(
            Post,
            advance,
            new { status = "failed", rowVersion = version },
            HttpStatusCode.BadRequest,
            "The next step is one of"
        );
        foreach (var step in new[] { "arrived_at_pickup", "picked_up", "arrived_at_customer" })
            version = await AdvanceAsync(shop.Admin, id, version, step);
        // 18+ in the bag: no delivery without the ID check.
        await shop.Admin.RefusedAsync(
            Post,
            advance,
            new
            {
                status = "delivered",
                ageChecked = false,
                rowVersion = version,
            },
            HttpStatusCode.BadRequest,
            "confirm that you checked the customer's ID"
        );
        await AdvanceAsync(shop.Admin, id, version, "delivered", ageChecked: true);

        var delivered = await shop.Admin.GetObjectAsync(ApiRoutes.Shipment.With(id));
        Assert.Equal("delivered", delivered["status"]!.GetValue<string>());
        Assert.True(delivered["ageChecked"]!.GetValue<bool>());
        Assert.NotNull(delivered["pickedUpAt"]);
        Assert.Equal("available", await ShiftAsync(shop.Admin, courier));
        Assert.Equal(1, await OutboxCountAsync("shipment-delivered", id));
        Assert.Equal(5, await OutboxCountAsync("shipment-status-changed", id));
        await shop.Admin.RefusedAsync(
            Post,
            advance,
            new
            {
                status = "delivered",
                ageChecked = true,
                rowVersion = delivered.RowVersion(),
            },
            HttpStatusCode.Conflict,
            "The shipment has ended"
        );

        // The runner's reaction: the order is delivered; the event again changes nothing. (The steps before delivered
        // never reach ordering now: the runner's handler skips them before calling the service.)
        Assert.Equal(
            "ready",
            (await shop.Admin.GetObjectAsync(ApiRoutes.Order.With(orderId)))[
                "status"
            ]!.GetValue<string>()
        );
        for (var i = 0; i < 2; i++)
            Assert.True((await CompleteAsync(orderId, delivered: true, null)).IsSuccess);
        var order = await shop.Admin.GetObjectAsync(ApiRoutes.Order.With(orderId));
        Assert.Equal("delivered", order["status"]!.GetValue<string>());
        Assert.NotNull(order["deliveredAt"]);
        // A delivered order cannot fail any more: a refusal the runner retries, then dead-letters.
        var late = await CompleteAsync(orderId, delivered: false, "Customer absent");
        Assert.Equal(ErrorKind.Conflict, late.Error?.Kind);
    }

    [Fact]
    public async Task Unassign_only_before_pickup_and_a_failed_delivery_ends_the_order()
    {
        var shop = await ShopAsync(api);
        var orderId = await PackedOrderAsync(shop, (shop.Bread, 2));
        var shipment = await ShipmentAsync(api, shop.Admin, orderId);
        var id = shipment["id"]!.GetValue<Guid>();
        var unassign = ApiRoutes.ShipmentUnassign.With(id);
        var fail = ApiRoutes.ShipmentFail.With(id);
        var courier = await CourierAsync(shop.Admin, "bike", cleared: true, 50.0514m, 19.9447m);

        await shop.Admin.RefusedAsync(
            Post,
            unassign,
            new { rowVersion = shipment.RowVersion() },
            HttpStatusCode.Conflict,
            "has not picked the goods up yet"
        );
        var version = await AssignAsync(shop.Admin, id, courier, shipment.RowVersion());
        await shop.Admin.RefusedAsync(
            Post,
            fail,
            new { reason = "Customer absent", rowVersion = version },
            HttpStatusCode.Conflict,
            "Only a shipment the courier has picked up can fail"
        );
        var unassigned = (
            await shop.Admin.PostAsync(unassign, new { rowVersion = version })
        ).RowVersion();
        var available = await shop.Admin.GetObjectAsync(ApiRoutes.Shipment.With(id));
        Assert.Equal("available", available["status"]!.GetValue<string>());
        Assert.Null(available["courierId"]);
        Assert.Equal("available", await ShiftAsync(shop.Admin, courier));

        version = await AssignAsync(shop.Admin, id, courier, unassigned);
        version = await AdvanceAsync(shop.Admin, id, version, "arrived_at_pickup");
        version = await AdvanceAsync(shop.Admin, id, version, "picked_up");
        await shop.Admin.RefusedAsync(
            Post,
            unassign,
            new { rowVersion = version },
            HttpStatusCode.Conflict,
            "has not picked the goods up yet"
        );
        await shop.Admin.RefusedAsync(
            Post,
            fail,
            new { reason = " ", rowVersion = version },
            HttpStatusCode.BadRequest,
            "Say why the delivery failed"
        );
        await shop.Admin.PostAsync(fail, new { reason = "Customer absent", rowVersion = version });
        var failed = await shop.Admin.GetObjectAsync(ApiRoutes.Shipment.With(id));
        Assert.Equal("failed", failed["status"]!.GetValue<string>());
        Assert.Equal("Customer absent", failed["failureReason"]!.GetValue<string>());
        Assert.Equal("available", await ShiftAsync(shop.Admin, courier));

        Assert.True((await CompleteAsync(orderId, delivered: false, "Customer absent")).IsSuccess);
        var order = await shop.Admin.GetObjectAsync(ApiRoutes.Order.With(orderId));
        Assert.Equal("failed", order["status"]!.GetValue<string>());
        Assert.Equal("Customer absent", order["failureReason"]!.GetValue<string>());
    }
}
