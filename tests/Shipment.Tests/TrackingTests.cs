using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shipment.Api.Endpoints;
using Shipment.Application.Tracking;
using Shipment.Domain.Common;
using Shipment.Domain.Tracking;
using Shipment.Geo;

namespace Shipment.Tests;

/// <summary>
/// Module tracking: the route the runner stores, the approximate fallback, the ETA, the simulator's switch and its
/// step along a line (spec 005). The runner's part is the application service called without a user, as its
/// handlers do.
/// </summary>
[Trait("verify", "tracking")]
[Collection(ApiCollection.Name)]
public sealed class TrackingTests(ApiFixture api)
{
    private const string ToPickup =
        """{"type":"LineString","coordinates":[[19.9450,50.0520],[19.9446,50.0513]]}""";
    private const string ToCustomer =
        """{"type":"LineString","coordinates":[[19.9446,50.0513],[19.9400,50.0600],[19.9377,50.0706]]}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A shipment with a bike courier assigned (no route yet).</summary>
    private async Task<(HttpClient Admin, Guid ShipmentId, Guid CourierId)> AssignedAsync()
    {
        var shop = await DispatchTests.ShopAsync(api);
        var shipment = await DispatchTests.ShipmentAsync(
            api,
            shop.Admin,
            await DispatchTests.PackedOrderAsync(shop, (shop.Bread, 1))
        );
        var courier = await DispatchTests.CourierAsync(
            shop.Admin,
            "bike",
            cleared: true,
            50.0520m,
            19.9450m
        );
        var id = shipment["id"]!.GetValue<Guid>();
        await DispatchTests.AssignAsync(shop.Admin, id, courier, shipment.RowVersion());
        return (shop.Admin, id, courier);
    }

    /// <summary>What the runner stores on courier-assigned once the router answered.</summary>
    internal static async Task RouteSetAsync(
        ApiFixture api,
        Guid shipmentId,
        Guid courierId,
        string profile,
        bool approximate,
        string line,
        int toPickupM,
        int toPickupS,
        int toCustomerM,
        int toCustomerS
    )
    {
        var result = await api.RunnerAsync<TrackingService, Result>(s =>
            s.SetRouteAsync(
                new SetRoute(
                    shipmentId,
                    courierId,
                    profile,
                    approximate,
                    new LegData(line, toPickupM, toPickupS),
                    new LegData(line, toCustomerM, toCustomerS)
                ),
                Ct
            )
        );
        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    private async Task RouteSetAsync(
        Guid shipmentId,
        Guid courierId,
        bool approximate,
        int toPickupM = 1000
    )
    {
        var result = await api.RunnerAsync<TrackingService, Result>(s =>
            s.SetRouteAsync(
                new SetRoute(
                    shipmentId,
                    courierId,
                    "bike",
                    approximate,
                    new LegData(ToPickup, toPickupM, 200),
                    new LegData(ToCustomer, 2000, 400)
                ),
                Ct
            )
        );
        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    private async Task PingAsync(
        Guid shipmentId,
        int travelledM,
        decimal latitude,
        decimal longitude
    )
    {
        var result = await api.RunnerAsync<TrackingService, Result>(s =>
            s.RecordPingAsync(shipmentId, RouteLeg.ToPickup, travelledM, latitude, longitude, Ct)
        );
        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    private Task<IReadOnlyList<SimulationStep>> DueAsync() =>
        api.RunnerAsync<TrackingService, IReadOnlyList<SimulationStep>>(s =>
            s.SimulationDueAsync(Ct)
        );

    /// <summary>The simulator on or off; the answer is its state.</summary>
    private static async Task<bool> SimulateAsync(HttpClient admin, Guid shipmentId, bool simulated)
    {
        var response = await admin.PutAsJsonAsync(
            ApiRoutes.ShipmentSimulation.With(shipmentId),
            new { simulated },
            Ct
        );
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<bool>(Ct);
    }

    private static Task<JsonObject> TrackingAsync(HttpClient admin, Guid shipmentId) =>
        admin.GetObjectAsync(ApiRoutes.ShipmentTracking.With(shipmentId));

    [Fact]
    public void A_step_moves_along_the_line_and_stops_at_its_end()
    {
        var line = GeoLine.Parse(ToCustomer);
        Assert.InRange(line.LengthM, 2100, 2300);
        Assert.Equal(new GeoPoint(19.9446, 50.0513), line.PointAt(-5));
        Assert.Equal(new GeoPoint(19.9377, 50.0706), line.PointAt(line.LengthM + 100));

        // Half the leg: half the line, between the first two points' latitudes and the last.
        var (travelled, half) = line.Step(legDistanceM: 2000, travelledM: 850, stepM: 150);
        Assert.Equal(1000, travelled);
        Assert.InRange(half.Latitude, 50.0513, 50.0706);
        Assert.InRange(
            GeoLine.DistanceM(new GeoPoint(19.9446, 50.0513), half),
            line.LengthM / 2 - 400,
            line.LengthM / 2 + 400
        );

        // Never past the leg: the last step ends at the customer.
        var (end, point) = line.Step(legDistanceM: 2000, travelledM: 1950, stepM: 150);
        Assert.Equal(2000, end);
        Assert.Equal(new GeoPoint(19.9377, 50.0706), point);

        var straight = GeoLine.Straight(
            new GeoPoint(19.9446, 50.0513),
            new GeoPoint(19.9377, 50.0706)
        );
        Assert.Equal(2, straight.Points.Count);
        Assert.Equal(straight.LengthM, GeoLine.Parse(straight.ToGeoJson()).LengthM, 3);
    }

    [Fact]
    public async Task A_route_is_stored_once_per_courier_and_the_eta_follows_the_courier()
    {
        var (admin, id, courier) = await AssignedAsync();
        await admin.RefusedAsync(
            HttpMethod.Put,
            ApiRoutes.ShipmentSimulation.With(id),
            new { simulated = true },
            HttpStatusCode.NotFound,
            "No route yet"
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await admin.GetAsync(ApiRoutes.ShipmentTracking.With(id), Ct)).StatusCode
        );

        // What the runner asks the router for: from the courier's last position, by the vehicle's profile.
        var request = await api.RunnerAsync<TrackingService, RouteRequest?>(s =>
            s.RouteRequestAsync(id, Ct)
        );
        Assert.NotNull(request);
        Assert.Equal(courier, request.CourierId);
        Assert.Equal("bike", request.Profile);
        Assert.Equal(50.0520m, request.CourierLatitude);

        await RouteSetAsync(id, courier, approximate: true);
        // A repeated courier-assigned: nothing changes (a different distance would show).
        await RouteSetAsync(id, courier, approximate: false, toPickupM: 5000);
        var tracking = await TrackingAsync(admin, id);
        Assert.True(tracking["approximate"]!.GetValue<bool>());
        Assert.Equal(1000, tracking["toPickupDistanceM"]!.GetValue<int>());
        Assert.Equal("pickup", tracking["nextStop"]!.GetValue<string>());
        Assert.Equal(3000, tracking["remainingM"]!.GetValue<int>());
        Assert.Equal(600, tracking["remainingS"]!.GetValue<int>());
        // Promised: assigned + both legs (600 s) + 10 minutes.
        var promised = tracking["promisedAt"]!.GetValue<DateTimeOffset>();
        var assigned = tracking["assignedAt"]!.GetValue<DateTimeOffset>();
        Assert.Equal(TimeSpan.FromSeconds(600) + TimeSpan.FromMinutes(10), promised - assigned);
        // The legs are GeoJSON text.
        Assert.Equal(
            "LineString",
            JsonNode.Parse(tracking["toCustomer"]!.GetValue<string>())!["type"]!.GetValue<string>()
        );
        Assert.Contains(
            await admin.ListAsync(ApiRoutes.Tracking),
            t => t!["shipmentId"]!.GetValue<Guid>() == id
        );

        // The simulator: on (twice: the same state is a no-op), listed with its leg; half of the way to the pickup point.
        Assert.True(await SimulateAsync(admin, id, true));
        Assert.True(await SimulateAsync(admin, id, true));
        var due = Assert.Single(await DueAsync(), s => s.ShipmentId == id);
        Assert.Equal(RouteLeg.ToPickup, due.Leg);
        Assert.Equal(0, due.TravelledM);
        await PingAsync(id, 500, 50.0516m, 19.9448m);
        tracking = await TrackingAsync(admin, id);
        Assert.Equal(2500, tracking["remainingM"]!.GetValue<int>());
        Assert.Equal(500, tracking["remainingS"]!.GetValue<int>());
        Assert.Equal(50.0516m, tracking["latitude"]!.GetValue<decimal>());
        Assert.False(tracking["late"]!.GetValue<bool>());
        var ping = Assert.Single(await admin.ListAsync(ApiRoutes.ShipmentPings.With(id)));
        Assert.Equal("to_pickup", ping!["leg"]!.GetValue<string>());

        // At the end of the leg it waits for the status button: no longer due.
        await PingAsync(id, 1000, 50.0513m, 19.9446m);
        Assert.DoesNotContain(await DueAsync(), s => s.ShipmentId == id);
        Assert.False(await SimulateAsync(admin, id, false));
        Assert.False((await TrackingAsync(admin, id))["simulated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Another_courier_replaces_the_route_and_the_simulator_starts_over()
    {
        var (admin, id, courier) = await AssignedAsync();
        await RouteSetAsync(id, courier, approximate: false);
        Assert.True(await SimulateAsync(admin, id, true));
        await PingAsync(id, 400, 50.0516m, 19.9448m);

        var other = await DispatchTests.CourierAsync(
            admin,
            "bike",
            cleared: true,
            50.0600m,
            19.9400m
        );
        await RouteSetAsync(id, other, approximate: false, toPickupM: 1500);
        var tracking = await TrackingAsync(admin, id);
        Assert.Equal(other, tracking["courierId"]!.GetValue<Guid>());
        Assert.Equal(1500, tracking["toPickupDistanceM"]!.GetValue<int>());
        Assert.False(tracking["simulated"]!.GetValue<bool>());
        Assert.False(tracking["approximate"]!.GetValue<bool>());
    }
}
