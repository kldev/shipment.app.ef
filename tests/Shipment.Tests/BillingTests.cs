using System.Net;
using System.Text.Json.Nodes;
using Npgsql;
using Shipment.Api.Endpoints;
using Shipment.Application.Billing;
using Shipment.Domain.Common;

namespace Shipment.Tests;

/// <summary>
/// Module billing: the ledger of an order - authorization, capture of the packed goods and the fee, refunds of the
/// missing lines, full refunds, commission and payout - and its summaries (spec 006). The runner's part is the
/// application service called with an event id and without a user, as its handlers do.
/// </summary>
[Trait("verify", "billing")]
[Collection(ApiCollection.Name)]
public sealed class BillingTests(ApiFixture api)
{
    // DispatchTests.ShopAsync: every product costs 5.00, the address is in the second fee band (8.99), 15 % commission.
    private const long Price = 500;
    private const long Fee = 899;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The summaries count by the operator's day (Europe/Warsaw).
    private static string Today => OperatorTime.Today(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd");

    private async Task BookAsync(Func<BillingService, Task<Result>> reaction)
    {
        var result = await api.RunnerAsync(reaction);
        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    private Task AuthorizeAsync(Guid eventId, Guid orderId) =>
        BookAsync(s => s.AuthorizeAsync(eventId, orderId, Ct));

    private Task SettleAsync(Guid eventId, Guid orderId) =>
        BookAsync(s => s.SettleAsync(eventId, orderId, Ct));

    private Task RefundAsync(Guid eventId, string messageType, Guid orderId, string reason) =>
        BookAsync(s => s.RefundAsync(eventId, messageType, orderId, reason, Ct));

    private Task SettleDeliveryAsync(Guid eventId, Guid shipmentId) =>
        BookAsync(s => s.SettleDeliveryAsync(eventId, shipmentId, Ct));

    private static async Task<List<JsonObject>> EntriesAsync(HttpClient admin, Guid orderId) =>
        (await admin.ListAsync($"{ApiRoutes.BillingEntries}?orderId={orderId}"))
            .Select(e => e!.AsObject())
            .OrderBy(e => e["id"]!.GetValue<long>())
            .ToList();

    private static Task<JsonObject> OrderBillingAsync(HttpClient admin, Guid orderId) =>
        admin.GetObjectAsync(ApiRoutes.OrderBilling.With(orderId));

    private static long Amount(IEnumerable<JsonObject> entries, string kind) =>
        entries
            .Where(e => e["kind"]!.GetValue<string>() == kind)
            .Sum(e => e["amountMinor"]!.GetValue<long>());

    /// <summary>The order delivered by a new courier of the vehicle over a route of the given legs; the shipment id.</summary>
    private async Task<Guid> DeliveredAsync(
        DispatchTests.Shop shop,
        Guid orderId,
        string vehicle,
        int toPickupM,
        int toCustomerM
    )
    {
        var shipment = await DispatchTests.ShipmentAsync(api, shop.Admin, orderId);
        var id = shipment["id"]!.GetValue<Guid>();
        var courier = await DispatchTests.CourierAsync(
            shop.Admin,
            vehicle,
            cleared: true,
            50.0514m,
            19.9447m
        );
        var version = await DispatchTests.AssignAsync(
            shop.Admin,
            id,
            courier,
            shipment.RowVersion()
        );
        await TrackingTests.RouteSetAsync(
            api,
            id,
            courier,
            vehicle == "bike" ? "bike" : "car",
            approximate: false,
            """{"type":"LineString","coordinates":[[19.9447,50.0514],[19.9446,50.0513]]}""",
            toPickupM,
            100,
            toCustomerM,
            200
        );
        foreach (
            var step in new[]
            {
                "arrived_at_pickup",
                "picked_up",
                "arrived_at_customer",
                "delivered",
            }
        )
            version = await DispatchTests.AdvanceAsync(
                shop.Admin,
                id,
                version,
                step,
                ageChecked: true
            );
        return id;
    }

    [Fact]
    public async Task A_partial_pick_captures_the_packed_goods_and_the_fee_and_refunds_exactly_the_missing_lines()
    {
        var shop = await DispatchTests.ShopAsync(api);
        var orderId = await DispatchTests.PackedAsync(
            shop,
            [(shop.Bread, 2), (shop.Wine, 1), (shop.Crate, 1)],
            [1, 1, 0]
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await shop.Admin.GetAsync(ApiRoutes.OrderBilling.With(orderId), Ct)).StatusCode
        );

        var placed = Guid.NewGuid();
        var picked = Guid.NewGuid();
        await AuthorizeAsync(placed, orderId);
        await SettleAsync(picked, orderId);
        // The same events again, and order-picked under another id: nothing new.
        await AuthorizeAsync(placed, orderId);
        await SettleAsync(picked, orderId);
        await SettleAsync(Guid.NewGuid(), orderId);

        var entries = await EntriesAsync(shop.Admin, orderId);
        Assert.Equal(5, entries.Count);
        Assert.Equal(4 * Price + Fee, Amount(entries, "authorization"));
        Assert.Equal(2 * Price, Amount(entries, "capture_goods"));
        Assert.Equal(Fee, Amount(entries, "capture_fee"));
        var refunds = entries.Where(e => e["kind"]!.GetValue<string>() == "refund").ToList();
        Assert.Equal([1, 3], refunds.Select(r => r["lineNo"]!.GetValue<int>()));
        Assert.Equal([1, 1], refunds.Select(r => r["quantity"]!.GetValue<int>()));
        Assert.Equal(
            [shop.Bread, shop.Crate],
            refunds.Select(r => r["productId"]!.GetValue<Guid>())
        );
        Assert.Equal([Price, Price], refunds.Select(r => r["amountMinor"]!.GetValue<long>()));
        Assert.Equal(
            2,
            (
                await shop.Admin.ListAsync(
                    $"{ApiRoutes.BillingEntries}?orderId={orderId}&kind=refund"
                )
            ).Count
        );

        var billing = await OrderBillingAsync(shop.Admin, orderId);
        // Authorized = captured + refunded; the customer pays the packed goods and the fee.
        Assert.Equal(2 * Price + Fee, billing["paidMinor"]!.GetValue<long>());
        Assert.Equal(2 * Price, billing["refundedMinor"]!.GetValue<long>());
        Assert.Null(billing["marginMinor"]);
    }

    [Fact]
    public async Task A_cancelled_order_and_a_failed_delivery_are_refunded_in_full_once()
    {
        var shop = await DispatchTests.ShopAsync(api);
        var (cancelledId, placed) = await DispatchTests.PlaceAsync(shop, [(shop.Bread, 3)]);
        await shop.Admin.PostAsync(
            ApiRoutes.OrderCancel.With(cancelledId),
            new { reason = "Changed my mind", rowVersion = placed.RowVersion() }
        );
        var cancelled = Guid.NewGuid();
        await AuthorizeAsync(Guid.NewGuid(), cancelledId);
        await RefundAsync(cancelled, "order-cancelled", cancelledId, "Changed my mind");
        await RefundAsync(cancelled, "order-cancelled", cancelledId, "Changed my mind");
        await RefundAsync(Guid.NewGuid(), "order-cancelled", cancelledId, "again");
        var entries = await EntriesAsync(shop.Admin, cancelledId);
        Assert.Equal(2, entries.Count);
        Assert.Equal(3 * Price + Fee, Amount(entries, "refund"));
        Assert.Equal(
            0,
            (await OrderBillingAsync(shop.Admin, cancelledId))["paidMinor"]!.GetValue<long>()
        );

        // Packed without one line, then the delivery failed: the captures come back too.
        var failedId = await DispatchTests.PackedAsync(
            shop,
            [(shop.Bread, 1), (shop.Wine, 1)],
            [1, 0]
        );
        await AuthorizeAsync(Guid.NewGuid(), failedId);
        await SettleAsync(Guid.NewGuid(), failedId);
        await RefundAsync(Guid.NewGuid(), "shipment-failed", failedId, "Customer absent");
        var failed = await OrderBillingAsync(shop.Admin, failedId);
        Assert.Equal(0, failed["paidMinor"]!.GetValue<long>());
        Assert.Equal(2 * Price + Fee, failed["refundedMinor"]!.GetValue<long>());
    }

    [Fact]
    public async Task Delivery_books_the_commission_on_the_goods_and_the_payout_by_vehicle_and_distance()
    {
        var shop = await DispatchTests.ShopAsync(api);
        var bikeOrder = await DispatchTests.PackedAsync(shop, [(shop.Bread, 2)], [2]);
        await AuthorizeAsync(Guid.NewGuid(), bikeOrder);
        await SettleAsync(Guid.NewGuid(), bikeOrder);
        var bikeShipment = await DeliveredAsync(shop, bikeOrder, "bike", 1000, 2000);
        var delivered = Guid.NewGuid();
        await SettleDeliveryAsync(delivered, bikeShipment);
        await SettleDeliveryAsync(delivered, bikeShipment);
        await SettleDeliveryAsync(Guid.NewGuid(), bikeShipment);

        var bike = await OrderBillingAsync(shop.Admin, bikeOrder);
        // 15 % of the packed goods, never of the fee.
        Assert.Equal(150, bike["commissionMinor"]!.GetValue<long>());
        Assert.Equal(2 * Price - 150, bike["partnerPayableMinor"]!.GetValue<long>());
        // Bike: 6.00 + 1.20 per km of 3 km.
        Assert.Equal(600 + 360, bike["payoutMinor"]!.GetValue<long>());
        Assert.Equal(Fee + 150 - 960, bike["marginMinor"]!.GetValue<long>());
        var payout = (await EntriesAsync(shop.Admin, bikeOrder)).Single(e =>
            e["kind"]!.GetValue<string>() == "payout"
        );
        Assert.Equal(3000, payout["basis"]!.GetValue<long>());

        // A crate needs a car: 8.00 + 1.60 per km of 2.5 km.
        var carOrder = await DispatchTests.PackedAsync(shop, [(shop.Crate, 1)], [1]);
        await AuthorizeAsync(Guid.NewGuid(), carOrder);
        await SettleAsync(Guid.NewGuid(), carOrder);
        await SettleDeliveryAsync(
            Guid.NewGuid(),
            await DeliveredAsync(shop, carOrder, "car", 500, 2000)
        );
        Assert.Equal(
            800 + 400,
            (await OrderBillingAsync(shop.Admin, carOrder))["payoutMinor"]!.GetValue<long>()
        );

        // The summaries add up to the ledger: the partner's two deliveries, today's day.
        var partner = Assert.Single(
            await shop.Admin.ListAsync(
                $"{ApiRoutes.BillingPartnerBalances}?from={Today}&to={Today}"
            ),
            p =>
                p!["partnerName"]!.GetValue<string>() == "Dispatch Test Grocer"
                && p["goodsMinor"]!.GetValue<long>() == 3 * Price
                && p["deliveries"]!.GetValue<int>() == 2
        )!;
        Assert.Equal(150 + 75, partner["commissionMinor"]!.GetValue<long>());
        Assert.Equal(3 * Price - 225, partner["payableMinor"]!.GetValue<long>());

        var day = Assert.Single(
            await shop.Admin.ListAsync($"{ApiRoutes.BillingDailySummary}?from={Today}&to={Today}")
        )!;
        var ledger = await api.ScalarAsync<long>(
            "SELECT coalesce(sum(amount_minor), 0)::bigint FROM billing.entries WHERE kind = 'payout' "
                + "AND (booked_at AT TIME ZONE 'Europe/Warsaw')::date = (now() AT TIME ZONE 'Europe/Warsaw')::date"
        );
        Assert.Equal(ledger, day["payoutMinor"]!.GetValue<long>());
        var couriers = await shop.Admin.ListAsync(
            $"{ApiRoutes.BillingCourierPayouts}?from={Today}&to={Today}"
        );
        Assert.Equal(ledger, couriers.Sum(c => c!["payoutMinor"]!.GetValue<long>()));
    }

    [Fact]
    public async Task The_tip_is_charged_as_chosen_and_goes_to_the_courier_in_full()
    {
        var shop = await DispatchTests.ShopAsync(api);
        await shop.Admin.RefusedAsync(
            HttpMethod.Post,
            ApiRoutes.Orders,
            new
            {
                id = Guid.CreateVersion7(),
                customerId = shop.CustomerId,
                addressId = shop.AddressId,
                pickupPointId = shop.PointId,
                lines = new[] { new { productId = shop.Bread, quantity = 1 } },
                tipPercent = 7,
            },
            HttpStatusCode.BadRequest,
            "The tip is 0, 5 or 10 percent"
        );

        // 10 % of 4 x 5.00 ordered = 2.00; one line short at picking - the tip stays as chosen.
        var orderId = await DispatchTests.PackedAsync(
            shop,
            [(shop.Bread, 3), (shop.Wine, 1)],
            [2, 1],
            tipPercent: 10
        );
        var order = await shop.Admin.GetObjectAsync(ApiRoutes.Order.With(orderId));
        Assert.Equal(10, order["tipPercent"]!.GetValue<int>());
        Assert.Equal(200, order["tipMinor"]!.GetValue<long>());
        Assert.Equal(3 * Price + Fee + 200, order["totalMinor"]!.GetValue<long>());

        await AuthorizeAsync(Guid.NewGuid(), orderId);
        await SettleAsync(Guid.NewGuid(), orderId);
        var shipment = await DeliveredAsync(shop, orderId, "bike", 1000, 2000);
        await SettleDeliveryAsync(Guid.NewGuid(), shipment);

        var entries = await EntriesAsync(shop.Admin, orderId);
        Assert.Equal(4 * Price + Fee + 200, Amount(entries, "authorization"));
        Assert.Equal(200, Amount(entries, "capture_tip"));
        Assert.Equal(Price, Amount(entries, "refund"));
        var tip = entries.Single(e => e["kind"]!.GetValue<string>() == "tip");
        Assert.Equal(200, tip["amountMinor"]!.GetValue<long>());
        var payout = entries.Single(e => e["kind"]!.GetValue<string>() == "payout");
        Assert.Equal(payout["courierId"]!.GetValue<Guid>(), tip["courierId"]!.GetValue<Guid>());

        var billing = await OrderBillingAsync(shop.Admin, orderId);
        Assert.Equal(3 * Price + Fee + 200, billing["paidMinor"]!.GetValue<long>());
        // The tip passes through: the margin is fee + commission - payout, as without it.
        Assert.Equal(
            Fee
                + billing["commissionMinor"]!.GetValue<long>()
                - billing["payoutMinor"]!.GetValue<long>(),
            billing["marginMinor"]!.GetValue<long>()
        );
        var courier = Assert.Single(
            await shop.Admin.ListAsync(
                $"{ApiRoutes.BillingCourierPayouts}?from={Today}&to={Today}"
            ),
            c => c!["courierId"]!.GetValue<Guid>() == tip["courierId"]!.GetValue<Guid>()
        )!;
        Assert.Equal(200, courier["tipMinor"]!.GetValue<long>());
    }

    [Fact]
    public async Task The_ledger_is_append_only()
    {
        var shop = await DispatchTests.ShopAsync(api);
        var orderId = await DispatchTests.PackedAsync(shop, [(shop.Bread, 1)], [1]);
        await AuthorizeAsync(Guid.NewGuid(), orderId);

        // Even the superuser: the trigger refuses, whatever the grants say.
        var update = await Assert.ThrowsAsync<PostgresException>(() =>
            api.ExecuteAsync(
                $"UPDATE billing.entries SET amount_minor = 1 WHERE order_id = '{orderId}'"
            )
        );
        Assert.Contains("append-only", update.MessageText);
        var delete = await Assert.ThrowsAsync<PostgresException>(() =>
            api.ExecuteAsync($"DELETE FROM billing.entries WHERE order_id = '{orderId}'")
        );
        Assert.Contains("append-only", delete.MessageText);
    }
}
