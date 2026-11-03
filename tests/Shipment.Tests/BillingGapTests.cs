using System.Text.Json.Nodes;
using Shipment.Api.Endpoints;
using Shipment.Application.Billing;
using Shipment.Domain.Common;

namespace Shipment.Tests;

/// <summary>billing cases the first tests leave out: rounding of commission and payout, partial refunds, tips refunded, races.</summary>
[Trait("verify", "billing")]
[Collection(ApiCollection.Name)]
public sealed class BillingGapTests(ApiFixture api)
{
    private const long Fee = 899;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task BookAsync(Func<BillingService, Task<Result>> reaction)
    {
        var result = await api.RunnerAsync(reaction);
        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    private static async Task<List<JsonObject>> EntriesAsync(HttpClient admin, Guid orderId) =>
        (await admin.ListAsync($"{ApiRoutes.BillingEntries}?orderId={orderId}"))
            .Select(e => e!.AsObject())
            .ToList();

    private static long Amount(IEnumerable<JsonObject> entries, string kind) =>
        entries
            .Where(e => e["kind"]!.GetValue<string>() == kind)
            .Sum(e => e["amountMinor"]!.GetValue<long>());

    /// <summary>Authorized, settled at picking, delivered by a new courier over legs of the given length, booked.</summary>
    private async Task DeliverAndSettleAsync(
        GapShop shop,
        Guid orderId,
        string vehicle,
        int toPickupM,
        int toCustomerM
    )
    {
        await BookAsync(s => s.AuthorizeAsync(Guid.NewGuid(), orderId, Ct));
        await BookAsync(s => s.SettleAsync(Guid.NewGuid(), orderId, Ct));
        var shipment = await DispatchTests.ShipmentAsync(api, shop.Admin, orderId);
        Assert.Equal(vehicle, shipment["requiredVehicle"]!.GetValue<string>());
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
            60,
            toCustomerM,
            60
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
        await BookAsync(s => s.SettleDeliveryAsync(Guid.NewGuid(), id, Ct));
    }

    [Fact]
    public async Task The_commission_is_rounded_half_away_from_zero()
    {
        // 15 % of 0.30 = 4.5 grosze -> 5.
        var shop = await GapShop.CreateAsync(api, commissionBps: 1500);
        var gum = await shop.ProductAsync("GUM", 30);
        var orderId = await DispatchTests.PackedAsync(shop.Dispatch, [(gum, 1)], [1]);
        await DeliverAndSettleAsync(shop, orderId, "bike", 500, 500);
        var billing = await shop.Admin.GetObjectAsync(ApiRoutes.OrderBilling.With(orderId));
        Assert.Equal(5, billing["commissionMinor"]!.GetValue<long>());
        Assert.Equal(25, billing["partnerPayableMinor"]!.GetValue<long>());
        // Bike: 6.00 + 1.20 per km of 1 km.
        Assert.Equal(720, billing["payoutMinor"]!.GetValue<long>());
    }

    [Fact]
    public async Task The_payout_per_km_is_rounded_half_away_from_zero()
    {
        // A van over 75 m: 12.00 + 2.20 * 0.075 = 12.00 + 0.165 -> 12.17.
        var shop = await GapShop.CreateAsync(api);
        var crate = await shop.ProductAsync("CRATE", 500, weightG: 9000, sizeClass: "M");
        var orderId = await DispatchTests.PackedAsync(shop.Dispatch, [(crate, 5)], [5]);
        await DeliverAndSettleAsync(shop, orderId, "van", 25, 50);
        var payout = (await EntriesAsync(shop.Admin, orderId)).Single(e =>
            e["kind"]!.GetValue<string>() == "payout"
        );
        Assert.Equal(1217, payout["amountMinor"]!.GetValue<long>());
        Assert.Equal(75, payout["basis"]!.GetValue<long>());
    }

    [Fact]
    public async Task A_line_packed_in_part_is_refunded_for_the_missing_quantity_only()
    {
        var shop = await GapShop.CreateAsync(api);
        var bread = await shop.ProductAsync("BREAD", 500);
        var milk = await shop.ProductAsync("MILK", 300);
        var orderId = await DispatchTests.PackedAsync(
            shop.Dispatch,
            [(bread, 3), (milk, 2)],
            [1, 2]
        );
        await BookAsync(s => s.AuthorizeAsync(Guid.NewGuid(), orderId, Ct));
        await BookAsync(s => s.SettleAsync(Guid.NewGuid(), orderId, Ct));
        var entries = await EntriesAsync(shop.Admin, orderId);
        var refund = Assert.Single(entries, e => e["kind"]!.GetValue<string>() == "refund");
        Assert.Equal(1, refund["lineNo"]!.GetValue<int>());
        Assert.Equal(2, refund["quantity"]!.GetValue<int>());
        Assert.Equal(2 * 500, refund["amountMinor"]!.GetValue<long>());
        Assert.Equal(500 + 2 * 300, Amount(entries, "capture_goods"));
        Assert.Equal(Fee, Amount(entries, "capture_fee"));
        Assert.Equal(
            Amount(entries, "authorization"),
            Amount(entries, "capture_goods")
                + Amount(entries, "capture_fee")
                + Amount(entries, "capture_tip")
                + Amount(entries, "refund")
        );
    }

    [Fact]
    public async Task A_cancelled_order_gets_its_tip_back_too()
    {
        var shop = await GapShop.CreateAsync(api);
        var bread = await shop.ProductAsync("BREAD", 500);
        var (orderId, placed) = await DispatchTests.PlaceAsync(
            shop.Dispatch,
            [(bread, 3)],
            tipPercent: 10
        );
        await shop.Admin.PostAsync(
            ApiRoutes.OrderCancel.With(orderId),
            new { reason = "Changed my mind", rowVersion = placed.RowVersion() }
        );
        await BookAsync(s => s.AuthorizeAsync(Guid.NewGuid(), orderId, Ct));
        await BookAsync(s =>
            s.RefundAsync(Guid.NewGuid(), "order-cancelled", orderId, "Changed my mind", Ct)
        );
        var entries = await EntriesAsync(shop.Admin, orderId);
        Assert.Equal(3 * 500 + Fee + 150, Amount(entries, "authorization"));
        Assert.Equal(3 * 500 + Fee + 150, Amount(entries, "refund"));
        var billing = await shop.Admin.GetObjectAsync(ApiRoutes.OrderBilling.With(orderId));
        Assert.Equal(0, billing["paidMinor"]!.GetValue<long>());
    }

    [Fact]
    public async Task One_authorization_per_order_whatever_the_event_id_and_also_when_two_arrive_at_once()
    {
        var shop = await GapShop.CreateAsync(api);
        var bread = await shop.ProductAsync("BREAD", 500);
        var (orderId, _) = await DispatchTests.PlaceAsync(shop.Dispatch, [(bread, 1)]);
        var eventId = Guid.NewGuid();
        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 2)
                .Select(_ =>
                    Task.Run(
                        () =>
                            api.RunnerAsync<BillingService, Result>(s =>
                                s.AuthorizeAsync(eventId, orderId, Ct)
                            ),
                        Ct
                    )
                )
        );
        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error?.Message));
        await BookAsync(s => s.AuthorizeAsync(Guid.NewGuid(), orderId, Ct));
        var entries = await EntriesAsync(shop.Admin, orderId);
        Assert.Single(entries, e => e["kind"]!.GetValue<string>() == "authorization");
        Assert.Equal(500 + Fee, Amount(entries, "authorization"));
    }

    [Fact]
    public async Task Two_different_order_picked_events_at_once_capture_once()
    {
        var shop = await GapShop.CreateAsync(api);
        var bread = await shop.ProductAsync("BREAD", 500);
        var orderId = await DispatchTests.PackedAsync(shop.Dispatch, [(bread, 2)], [1]);
        await BookAsync(s => s.AuthorizeAsync(Guid.NewGuid(), orderId, Ct));
        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 2)
                .Select(_ =>
                    Task.Run(
                        () =>
                            api.RunnerAsync<BillingService, Result>(s =>
                                s.SettleAsync(Guid.NewGuid(), orderId, Ct)
                            ),
                        Ct
                    )
                )
        );
        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error?.Message));
        var entries = await EntriesAsync(shop.Admin, orderId);
        Assert.Single(entries, e => e["kind"]!.GetValue<string>() == "capture_goods");
        Assert.Single(entries, e => e["kind"]!.GetValue<string>() == "capture_fee");
        Assert.Single(entries, e => e["kind"]!.GetValue<string>() == "refund");
    }
}
