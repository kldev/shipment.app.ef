using System.Net;
using Shipment.Api.Endpoints;

namespace Shipment.Tests;

/// <summary>Module carriers: couriers, their vehicles, shifts and positions (spec 002).</summary>
[Trait("verify", "carriers")]
[Collection(ApiCollection.Name)]
public sealed class CarriersTests(ApiFixture api)
{
    private static readonly HttpMethod Post = HttpMethod.Post;
    private static readonly HttpMethod Put = HttpMethod.Put;

    internal static object Courier(
        Guid id,
        string vehicleType = "bike",
        string? vehiclePlate = null,
        string? email = null
    ) =>
        new
        {
            id,
            displayName = "Kuba Rider",
            phone = "+48 700 100 200",
            email = email ?? $"{id:N}@couriers.example",
            vehicleType,
            vehiclePlate,
        };

    [Fact]
    public async Task Vehicle_types_carry_their_limits()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var types = await admin.ListAsync(ApiRoutes.VehicleTypes);
        Assert.Equal(
            ["bike", "car", "van"],
            types
                .OrderBy(t => t!["sortOrder"]!.GetValue<int>())
                .Select(t => t!["code"]!.GetValue<string>())
        );
        var bike = types.Single(t => t!["code"]!.GetValue<string>() == "bike")!;
        Assert.Equal(8000, bike["maxLoadG"]!.GetValue<int>());
        Assert.Equal("M", bike["maxSizeClass"]!.GetValue<string>());
        Assert.False(bike["needsLicence"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_courier_is_created_idempotently_and_a_car_needs_a_plate()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var id = Guid.CreateVersion7();
        var email = $"{id:N}@couriers.example";

        var created = await admin.PostAsync(
            ApiRoutes.Couriers,
            Courier(id, "car", "KR 1234A", email)
        );
        Assert.Equal(
            created.RowVersion(),
            (
                await admin.PostAsync(ApiRoutes.Couriers, Courier(id, "car", "kr 1234a", email))
            ).RowVersion()
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Couriers,
            Courier(id, "van", "KR 1234A", email),
            HttpStatusCode.Conflict,
            "already used by another courier"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Couriers,
            Courier(Guid.CreateVersion7(), email: email),
            HttpStatusCode.Conflict,
            "Another courier already uses this e-mail"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Couriers,
            Courier(Guid.CreateVersion7(), "car"),
            HttpStatusCode.BadRequest,
            "needs its registration plate"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Couriers,
            Courier(Guid.CreateVersion7(), "scooter"),
            HttpStatusCode.BadRequest,
            "Vehicle must be a bike, a car or a van"
        );

        var read = await admin.GetObjectAsync(ApiRoutes.Courier.With(id));
        Assert.Equal("KR1234A", read["vehiclePlate"]!.GetValue<string>());
        Assert.Equal("offline", read["shiftStatus"]!.GetValue<string>());
        Assert.Equal("Car", read["vehicleName"]!.GetValue<string>());
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await admin.GetAsync(
                    ApiRoutes.Courier.With(Guid.CreateVersion7()),
                    TestContext.Current.CancellationToken
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task Courier_rules_are_refused_with_their_messages()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        object Body(Guid id, string displayName, string phone, string email) =>
            new
            {
                id,
                displayName,
                phone,
                email,
                vehicleType = "bike",
            };
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Couriers,
            Body(Guid.CreateVersion7(), " ", "+48 700 000 000", "a@b.example"),
            HttpStatusCode.BadRequest,
            "Courier name is required"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Couriers,
            Body(Guid.CreateVersion7(), "N", "+48 700 000 000", "nope"),
            HttpStatusCode.BadRequest,
            "Enter a valid e-mail address"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Couriers,
            Body(Guid.CreateVersion7(), "N", "call me", "c@d.example"),
            HttpStatusCode.BadRequest,
            "Phone may contain digits"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Couriers,
            Body(Guid.Empty, "N", "+48 700 000 000", "e@f.example"),
            HttpStatusCode.BadRequest,
            "Courier id is required"
        );
        await admin.RefusedAsync(
            Put,
            ApiRoutes.CourierPosition.With(Guid.CreateVersion7()),
            new { latitude = 50m, longitude = 19m },
            HttpStatusCode.NotFound,
            "Courier not found"
        );
        await admin.RefusedAsync(
            Put,
            ApiRoutes.Courier.With(Guid.CreateVersion7()),
            new
            {
                displayName = "N",
                phone = "+48 700 000 000",
                email = "g@h.example",
                vehicleType = "bike",
                rowVersion = 1,
            },
            HttpStatusCode.NotFound,
            "Courier not found"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.CourierShiftStart.With(Guid.CreateVersion7()),
            new { rowVersion = 1 },
            HttpStatusCode.NotFound,
            "Courier not found"
        );
    }

    [Fact]
    public async Task A_shift_starts_and_ends_but_not_while_busy_and_the_vehicle_stays_while_busy()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var id = Guid.CreateVersion7();
        var created = await admin.PostAsync(ApiRoutes.Couriers, Courier(id));

        var started = await admin.PostAsync(
            ApiRoutes.CourierShiftStart.With(id),
            new { rowVersion = created.RowVersion() }
        );
        Assert.NotEqual(created.RowVersion(), started.RowVersion());
        // Already on shift: a no-op.
        Assert.Equal(
            started.RowVersion(),
            (
                await admin.PostAsync(
                    ApiRoutes.CourierShiftStart.With(id),
                    new { rowVersion = started.RowVersion() }
                )
            ).RowVersion()
        );
        var moved = await admin.PutAsync(
            ApiRoutes.CourierPosition.With(id),
            new { latitude = 50.0617m, longitude = 19.9373m }
        );
        await admin.RefusedAsync(
            Put,
            ApiRoutes.CourierPosition.With(id),
            new { latitude = 95m, longitude = 19m },
            HttpStatusCode.BadRequest,
            "Pick the place on the map"
        );
        var read = await admin.GetObjectAsync(ApiRoutes.Courier.With(id));
        Assert.Equal("available", read["shiftStatus"]!.GetValue<string>());
        Assert.Equal(50.0617m, read["lastLatitude"]!.GetValue<decimal>());
        Assert.Equal(0, read["positionAgeMinutes"]!.GetValue<int>());

        // Dispatch makes a courier busy (task 004); here the test plays dispatch.
        await api.ExecuteAsync(
            $"UPDATE carriers.couriers SET shift_status = 'busy' WHERE id = '{id}'"
        );
        var busy = (await admin.GetObjectAsync(ApiRoutes.Courier.With(id))).RowVersion();
        await admin.RefusedAsync(
            Post,
            ApiRoutes.CourierShiftEnd.With(id),
            new { rowVersion = busy },
            HttpStatusCode.Conflict,
            "end the shift after it"
        );
        await admin.RefusedAsync(
            Put,
            ApiRoutes.Courier.With(id),
            new
            {
                displayName = "Kuba Rider",
                phone = "+48 700 100 200",
                email = $"{id:N}@couriers.example",
                vehicleType = "car",
                vehiclePlate = "KR 9999",
                rowVersion = busy,
            },
            HttpStatusCode.Conflict,
            "change the vehicle after it"
        );

        await api.ExecuteAsync(
            $"UPDATE carriers.couriers SET shift_status = 'available' WHERE id = '{id}'"
        );
        var available = (await admin.GetObjectAsync(ApiRoutes.Courier.With(id))).RowVersion();
        await admin.RefusedAsync(
            Post,
            ApiRoutes.CourierShiftEnd.With(id),
            new { rowVersion = moved.RowVersion() },
            HttpStatusCode.Conflict,
            "was changed by someone else"
        );
        await admin.PostAsync(ApiRoutes.CourierShiftEnd.With(id), new { rowVersion = available });
        Assert.Equal(
            "offline",
            (await admin.GetObjectAsync(ApiRoutes.Courier.With(id)))[
                "shiftStatus"
            ]!.GetValue<string>()
        );
    }
}
