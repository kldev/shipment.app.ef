using System.Net;
using System.Text.Json.Nodes;
using Shipment.Api.Endpoints;

namespace Shipment.Tests;

/// <summary>carriers cases the first tests leave out: plates, e-mails, no-ops on edit, busy from a real assignment.</summary>
[Trait("verify", "carriers")]
[Collection(ApiCollection.Name)]
public sealed class CarriersGapTests(ApiFixture api)
{
    private static readonly HttpMethod Post = HttpMethod.Post;
    private static readonly HttpMethod Put = HttpMethod.Put;

    private static object Edit(
        Guid id,
        uint rowVersion,
        string vehicleType = "bike",
        string? vehiclePlate = null,
        string? email = null,
        string displayName = "Kuba Rider"
    ) =>
        new
        {
            displayName,
            phone = "+48 700 100 200",
            email = email ?? $"{id:N}@couriers.example",
            vehicleType,
            vehiclePlate,
            rowVersion,
        };

    private static Task<JsonObject> ReadAsync(HttpClient admin, Guid id) =>
        admin.GetObjectAsync(ApiRoutes.Courier.With(id));

    [Fact]
    public async Task A_bike_has_no_plate_and_a_van_needs_one()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var bike = Guid.CreateVersion7();
        await admin.PostAsync(ApiRoutes.Couriers, CarriersTests.Courier(bike, "bike", "KR 1"));
        Assert.Null((await ReadAsync(admin, bike))["vehiclePlate"]);
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Couriers,
            CarriersTests.Courier(Guid.CreateVersion7(), "van", "  "),
            HttpStatusCode.BadRequest,
            "needs its registration plate"
        );
    }

    [Fact]
    public async Task A_courier_email_is_unique_regardless_of_case_also_on_edit()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var first = Guid.CreateVersion7();
        var email = $"{first:N}@couriers.example";
        await admin.PostAsync(
            ApiRoutes.Couriers,
            CarriersTests.Courier(first, email: email.ToUpperInvariant())
        );
        Assert.Equal(email, (await ReadAsync(admin, first))["email"]!.GetValue<string>());
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Couriers,
            CarriersTests.Courier(Guid.CreateVersion7(), email: email),
            HttpStatusCode.Conflict,
            "Another courier already uses this e-mail"
        );
        var second = Guid.CreateVersion7();
        var created = await admin.PostAsync(ApiRoutes.Couriers, CarriersTests.Courier(second));
        await admin.RefusedAsync(
            Put,
            ApiRoutes.Courier.With(second),
            Edit(second, created.RowVersion(), email: email),
            HttpStatusCode.Conflict,
            "Another courier already uses this e-mail"
        );
    }

    [Fact]
    public async Task An_unchanged_courier_keeps_its_version_and_a_stale_edit_is_a_conflict()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var id = Guid.CreateVersion7();
        var created = await admin.PostAsync(ApiRoutes.Couriers, CarriersTests.Courier(id));
        var same = await admin.PutAsync(ApiRoutes.Courier.With(id), Edit(id, created.RowVersion()));
        Assert.Equal(created.RowVersion(), same.RowVersion());

        await admin.RefusedAsync(
            Put,
            ApiRoutes.Courier.With(id),
            Edit(id, created.RowVersion(), vehicleType: "car"),
            HttpStatusCode.BadRequest,
            "needs its registration plate"
        );
        var car = await admin.PutAsync(
            ApiRoutes.Courier.With(id),
            Edit(id, created.RowVersion(), vehicleType: "car", vehiclePlate: "kr 77 aa")
        );
        Assert.NotEqual(created.RowVersion(), car.RowVersion());
        Assert.Equal("KR77AA", (await ReadAsync(admin, id))["vehiclePlate"]!.GetValue<string>());
        await admin.RefusedAsync(
            Put,
            ApiRoutes.Courier.With(id),
            Edit(id, created.RowVersion(), displayName: "Someone"),
            HttpStatusCode.Conflict,
            "was changed by someone else"
        );
    }

    [Fact]
    public async Task Ending_a_shift_that_is_not_on_is_a_no_op()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var id = Guid.CreateVersion7();
        var created = await admin.PostAsync(ApiRoutes.Couriers, CarriersTests.Courier(id));
        var ended = await admin.PostAsync(
            ApiRoutes.CourierShiftEnd.With(id),
            new { rowVersion = created.RowVersion() }
        );
        Assert.Equal(created.RowVersion(), ended.RowVersion());
        Assert.Equal("offline", (await ReadAsync(admin, id))["shiftStatus"]!.GetValue<string>());
        await admin.RefusedAsync(
            Post,
            ApiRoutes.CourierShiftEnd.With(Guid.CreateVersion7()),
            new { rowVersion = 1 },
            HttpStatusCode.NotFound,
            "Courier not found"
        );
    }

    [Fact]
    public async Task A_courier_on_a_shipment_keeps_the_shift_and_the_vehicle_but_may_change_the_name()
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
            50.0514m,
            19.9447m
        );
        await DispatchTests.AssignAsync(
            shop.Admin,
            shipment["id"]!.GetValue<Guid>(),
            courier,
            shipment.RowVersion()
        );
        var busy = await ReadAsync(shop.Admin, courier);
        Assert.Equal("busy", busy["shiftStatus"]!.GetValue<string>());

        await shop.Admin.RefusedAsync(
            Post,
            ApiRoutes.CourierShiftEnd.With(courier),
            new { rowVersion = busy.RowVersion() },
            HttpStatusCode.Conflict,
            "end the shift after it"
        );
        await shop.Admin.RefusedAsync(
            Put,
            ApiRoutes.Courier.With(courier),
            Edit(courier, busy.RowVersion(), vehicleType: "van", vehiclePlate: "KR 9"),
            HttpStatusCode.Conflict,
            "change the vehicle after it"
        );
        // Starting the shift again does not make a busy courier available.
        await shop.Admin.PostAsync(
            ApiRoutes.CourierShiftStart.With(courier),
            new { rowVersion = busy.RowVersion() }
        );
        Assert.Equal(
            "busy",
            (await ReadAsync(shop.Admin, courier))["shiftStatus"]!.GetValue<string>()
        );
        var renamed = await shop.Admin.PutAsync(
            ApiRoutes.Courier.With(courier),
            Edit(
                courier,
                (await ReadAsync(shop.Admin, courier)).RowVersion(),
                displayName: "Kuba Rider Jr"
            )
        );
        Assert.NotEqual(busy.RowVersion(), renamed.RowVersion());
        var read = await ReadAsync(shop.Admin, courier);
        Assert.Equal("Kuba Rider Jr", read["displayName"]!.GetValue<string>());
        Assert.Equal("busy", read["shiftStatus"]!.GetValue<string>());
    }
}
