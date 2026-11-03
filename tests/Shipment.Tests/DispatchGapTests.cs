using System.Net;
using Shipment.Api.Endpoints;
using Shipment.Application.Dispatch;
using Shipment.Domain.Common;

namespace Shipment.Tests;

/// <summary>dispatch cases the first tests leave out: races, idempotency under concurrency, filters, other steps.</summary>
[Trait("verify", "dispatch")]
[Collection(ApiCollection.Name)]
public sealed class DispatchGapTests(ApiFixture api)
{
    private static readonly HttpMethod Post = HttpMethod.Post;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> ShiftAsync(HttpClient admin, Guid courierId) =>
        (await admin.GetObjectAsync(ApiRoutes.Courier.With(courierId)))[
            "shiftStatus"
        ]!.GetValue<string>();

    [Fact]
    public async Task Two_assignments_of_one_courier_at_once_one_wins_and_the_courier_is_busy_once()
    {
        var shop = await DispatchTests.ShopAsync(api);
        var first = await DispatchTests.ShipmentAsync(
            api,
            shop.Admin,
            await DispatchTests.PackedOrderAsync(shop, (shop.Bread, 1))
        );
        var second = await DispatchTests.ShipmentAsync(
            api,
            shop.Admin,
            await DispatchTests.PackedOrderAsync(shop, (shop.Bread, 1))
        );
        var courier = await DispatchTests.CourierAsync(
            shop.Admin,
            "bike",
            cleared: true,
            50.0514m,
            19.9447m
        );
        var firstId = first["id"]!.GetValue<Guid>();
        var secondId = second["id"]!.GetValue<Guid>();

        var statuses = await GapCalls.RaceAsync(
            () =>
                shop.Admin.SendRawAsync(
                    Post,
                    ApiRoutes.ShipmentAssign.With(firstId),
                    new { courierId = courier, rowVersion = first.RowVersion() }
                ),
            () =>
                shop.Admin.SendRawAsync(
                    Post,
                    ApiRoutes.ShipmentAssign.With(secondId),
                    new { courierId = courier, rowVersion = second.RowVersion() }
                )
        );
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);
        Assert.Equal("busy", await ShiftAsync(shop.Admin, courier));
        var withCourier = new List<Guid>();
        foreach (var id in new[] { firstId, secondId })
        {
            var shipment = await shop.Admin.GetObjectAsync(ApiRoutes.Shipment.With(id));
            if (shipment["courierId"] is not null)
                withCourier.Add(id);
            else
                Assert.Equal("available", shipment["status"]!.GetValue<string>());
        }
        Assert.Single(withCourier);
        Assert.Equal(
            1L,
            await api.ScalarAsync<long>(
                $"SELECT count(*) FROM messaging.outbox_messages WHERE message_type = 'courier-assigned' AND payload->>'courierId' = '{courier}'"
            )
        );
    }

    [Fact]
    public async Task A_repeated_order_picked_at_the_same_time_still_makes_one_shipment()
    {
        var shop = await DispatchTests.ShopAsync(api);
        var orderId = await DispatchTests.PackedOrderAsync(shop, (shop.Bread, 1));
        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 2)
                .Select(_ =>
                    Task.Run(
                        () =>
                            api.RunnerAsync<DispatchService, Result>(s =>
                                s.CreateForOrderAsync(orderId, Ct)
                            ),
                        Ct
                    )
                )
        );
        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error?.Message));
        Assert.Single(await shop.Admin.ListAsync($"{ApiRoutes.Shipments}?orderId={orderId}"));
    }

    [Fact]
    public async Task Two_steps_with_the_same_version_at_once_one_wins()
    {
        var shop = await DispatchTests.ShopAsync(api);
        var shipment = await DispatchTests.ShipmentAsync(
            api,
            shop.Admin,
            await DispatchTests.PackedOrderAsync(shop, (shop.Bread, 1))
        );
        var id = shipment["id"]!.GetValue<Guid>();
        var courier = await DispatchTests.CourierAsync(
            shop.Admin,
            "bike",
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
        var statuses = await GapCalls.RaceAsync(
            () =>
                shop.Admin.SendRawAsync(
                    Post,
                    ApiRoutes.ShipmentAdvance.With(id),
                    new { status = "arrived_at_pickup", rowVersion = version }
                ),
            () =>
                shop.Admin.SendRawAsync(
                    Post,
                    ApiRoutes.ShipmentAdvance.With(id),
                    new { status = "arrived_at_pickup", rowVersion = version }
                )
        );
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);
        Assert.Equal(
            2L,
            await api.ScalarAsync<long>(
                $"SELECT count(*) FROM messaging.outbox_messages WHERE message_type = 'shipment-status-changed' AND payload->>'shipmentId' = '{id}'"
            )
        );
    }

    [Fact]
    public async Task An_offline_courier_cannot_be_assigned_and_a_stale_step_is_a_conflict()
    {
        var shop = await DispatchTests.ShopAsync(api);
        var shipment = await DispatchTests.ShipmentAsync(
            api,
            shop.Admin,
            await DispatchTests.PackedOrderAsync(shop, (shop.Bread, 1))
        );
        var id = shipment["id"]!.GetValue<Guid>();
        var offline = await DispatchTests.CourierAsync(
            shop.Admin,
            "bike",
            cleared: true,
            50.0514m,
            19.9447m
        );
        await shop.Admin.PostAsync(
            ApiRoutes.CourierShiftEnd.With(offline),
            new
            {
                rowVersion = (
                    await shop.Admin.GetObjectAsync(ApiRoutes.Courier.With(offline))
                ).RowVersion(),
            }
        );
        await shop.Admin.RefusedAsync(
            Post,
            ApiRoutes.ShipmentAssign.With(id),
            new { courierId = offline, rowVersion = shipment.RowVersion() },
            HttpStatusCode.Conflict,
            "The courier cannot take this shipment now"
        );
        Assert.Equal("offline", await ShiftAsync(shop.Admin, offline));

        var courier = await DispatchTests.CourierAsync(
            shop.Admin,
            "bike",
            cleared: true,
            50.0514m,
            19.9447m
        );
        var assigned = await DispatchTests.AssignAsync(
            shop.Admin,
            id,
            courier,
            shipment.RowVersion()
        );
        await DispatchTests.AdvanceAsync(shop.Admin, id, assigned, "arrived_at_pickup");
        await shop.Admin.RefusedAsync(
            Post,
            ApiRoutes.ShipmentAdvance.With(id),
            new { status = "picked_up", rowVersion = assigned },
            HttpStatusCode.Conflict,
            "was changed by someone else"
        );
    }

    [Fact]
    public async Task Unassign_at_the_pickup_point_frees_the_courier_and_a_delivery_may_fail_at_the_door()
    {
        var shop = await DispatchTests.ShopAsync(api);
        var orderId = await DispatchTests.PackedOrderAsync(shop, (shop.Bread, 1));
        var shipment = await DispatchTests.ShipmentAsync(api, shop.Admin, orderId);
        var id = shipment["id"]!.GetValue<Guid>();
        var courier = await DispatchTests.CourierAsync(
            shop.Admin,
            "bike",
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
        version = await DispatchTests.AdvanceAsync(shop.Admin, id, version, "arrived_at_pickup");
        // Still before pickup: the courier may be taken off.
        version = (
            await shop.Admin.PostAsync(
                ApiRoutes.ShipmentUnassign.With(id),
                new { rowVersion = version }
            )
        ).RowVersion();
        var available = await shop.Admin.GetObjectAsync(ApiRoutes.Shipment.With(id));
        Assert.Equal("available", available["status"]!.GetValue<string>());
        Assert.Null(available["courierId"]);
        Assert.Equal("available", await ShiftAsync(shop.Admin, courier));

        // Assigned again, the steps start over; then the customer is not there.
        version = await DispatchTests.AssignAsync(shop.Admin, id, courier, version);
        foreach (var step in new[] { "arrived_at_pickup", "picked_up", "arrived_at_customer" })
            version = await DispatchTests.AdvanceAsync(shop.Admin, id, version, step);
        await shop.Admin.PostAsync(
            ApiRoutes.ShipmentFail.With(id),
            new { reason = "Customer absent", rowVersion = version }
        );
        Assert.Equal(
            "failed",
            (await shop.Admin.GetObjectAsync(ApiRoutes.Shipment.With(id)))[
                "status"
            ]!.GetValue<string>()
        );
        Assert.Equal("available", await ShiftAsync(shop.Admin, courier));
        // Each assignment asks tracking for a route (the second courier-assigned replaces the first route).
        Assert.Equal(
            2L,
            await api.ScalarAsync<long>(
                $"SELECT count(*) FROM messaging.outbox_messages WHERE message_type = 'courier-assigned' AND payload->>'shipmentId' = '{id}'"
            )
        );
    }

    [Fact]
    public async Task A_freed_courier_takes_the_next_shipment()
    {
        var shop = await DispatchTests.ShopAsync(api);
        var courier = await DispatchTests.CourierAsync(
            shop.Admin,
            "bike",
            cleared: true,
            50.0514m,
            19.9447m
        );
        for (var round = 0; round < 2; round++)
        {
            var shipment = await DispatchTests.ShipmentAsync(
                api,
                shop.Admin,
                await DispatchTests.PackedOrderAsync(shop, (shop.Bread, 1))
            );
            var id = shipment["id"]!.GetValue<Guid>();
            var version = await DispatchTests.AssignAsync(
                shop.Admin,
                id,
                courier,
                shipment.RowVersion()
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
                version = await DispatchTests.AdvanceAsync(shop.Admin, id, version, step);
            Assert.Equal("available", await ShiftAsync(shop.Admin, courier));
        }
    }

    [Fact]
    public async Task Shipments_filter_by_status_by_its_wire_name_and_an_unknown_one_is_a_400()
    {
        var shop = await DispatchTests.ShopAsync(api);
        var shipment = await DispatchTests.ShipmentAsync(
            api,
            shop.Admin,
            await DispatchTests.PackedOrderAsync(shop, (shop.Bread, 1))
        );
        var id = shipment["id"]!.GetValue<Guid>();
        var courier = await DispatchTests.CourierAsync(
            shop.Admin,
            "bike",
            cleared: true,
            50.0514m,
            19.9447m
        );
        await DispatchTests.AssignAsync(shop.Admin, id, courier, shipment.RowVersion());

        var assigned = await shop.Admin.ListAsync(
            $"{ApiRoutes.Shipments}?status=courier_assigned&courierId={courier}"
        );
        var row = Assert.Single(assigned)!;
        Assert.Equal(id, row["id"]!.GetValue<Guid>());
        Assert.Equal("courier_assigned", row["status"]!.GetValue<string>());
        Assert.All(
            await shop.Admin.ListAsync($"{ApiRoutes.Shipments}?status=courier_assigned"),
            s => Assert.Equal("courier_assigned", s!["status"]!.GetValue<string>())
        );
        Assert.Empty(
            await shop.Admin.ListAsync(
                $"{ApiRoutes.Shipments}?status=available&courierId={courier}"
            )
        );
        foreach (var bad in new[] { "nope", "CourierAssigned", "courier assigned" })
            Assert.Equal(
                HttpStatusCode.BadRequest,
                await shop.Admin.GetStatusAsync(
                    $"{ApiRoutes.Shipments}?status={Uri.EscapeDataString(bad)}"
                )
            );
    }
}
