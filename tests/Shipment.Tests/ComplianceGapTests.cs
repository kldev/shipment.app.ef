using System.Net;
using System.Text.Json.Nodes;
using Shipment.Api.Endpoints;
using Shipment.Domain.Common;

namespace Shipment.Tests;

/// <summary>compliance cases the first tests leave out: renewals, the last valid day, what blocks and what does not.</summary>
[Trait("verify", "compliance")]
[Collection(ApiCollection.Name)]
public sealed class ComplianceGapTests(ApiFixture api)
{
    private static DateOnly Today => OperatorTime.Today(DateTimeOffset.UtcNow);

    private static async Task<JsonObject> ClearanceAsync(HttpClient admin, Guid courierId) =>
        Assert
            .Single(await admin.ListAsync($"{ApiRoutes.CourierClearance}?courierId={courierId}"))!
            .AsObject();

    private static async Task<JsonObject> SetAsync(
        HttpClient admin,
        Guid courierId,
        string kind,
        string number,
        DateOnly validUntil
    )
    {
        var saved = await admin.PostAsync(
            ApiRoutes.CourierDocuments,
            new
            {
                id = Guid.CreateVersion7(),
                courierId,
                kind,
                number,
                validUntil = validUntil.ToString("yyyy-MM-dd"),
            }
        );
        return saved;
    }

    private static async Task<JsonObject> ReadAsync(
        HttpClient admin,
        Guid courierId,
        string kind
    ) =>
        Assert
            .Single(
                await admin.ListAsync(
                    $"{ApiRoutes.CourierDocuments}?courierId={courierId}&kind={kind}"
                )
            )!
            .AsObject();

    private async Task<(HttpClient Admin, Guid CourierId)> CourierAsync(string vehicle = "bike")
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var id = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.Couriers,
            CarriersTests.Courier(id, vehicle, vehicle == "bike" ? null : "KR 4242")
        );
        return (admin, id);
    }

    [Fact]
    public async Task Sending_the_same_document_again_keeps_it_verified_another_number_needs_verifying()
    {
        var (admin, courierId) = await CourierAsync();
        var until = Today.AddYears(1);
        await SetAsync(admin, courierId, "id_card", "ABC123456", until);
        await ComplianceTests.VerifyAsync(admin, await ReadAsync(admin, courierId, "id_card"));
        var verified = await ReadAsync(admin, courierId, "id_card");
        Assert.Equal("valid", verified["state"]!.GetValue<string>());

        // The same values (another client id, as the form makes a new one): a no-op.
        var same = await SetAsync(admin, courierId, "id_card", "ABC123456", until);
        Assert.Equal(verified.RowVersion(), same.RowVersion());
        Assert.Equal(
            "valid",
            (await ReadAsync(admin, courierId, "id_card"))["state"]!.GetValue<string>()
        );
        Assert.True((await ClearanceAsync(admin, courierId))["cleared"]!.GetValue<bool>());

        // Another number with the same date: a renewal - verify again.
        await SetAsync(admin, courierId, "id_card", "XYZ987654", until);
        var renewed = await ReadAsync(admin, courierId, "id_card");
        Assert.Equal(verified["id"]!.GetValue<Guid>(), renewed["id"]!.GetValue<Guid>());
        Assert.Equal("unverified", renewed["state"]!.GetValue<string>());
        Assert.Null(renewed["verifiedAt"]);
        var clearance = await ClearanceAsync(admin, courierId);
        Assert.False(clearance["cleared"]!.GetValue<bool>());
        Assert.Equal(
            "ID card not verified",
            Assert.Single(clearance["reasons"]!.AsArray())!.GetValue<string>()
        );
    }

    [Fact]
    public async Task A_document_valid_until_today_still_clears()
    {
        var (admin, courierId) = await CourierAsync();
        await SetAsync(admin, courierId, "id_card", "TODAY0001", Today);
        await ComplianceTests.VerifyAsync(admin, await ReadAsync(admin, courierId, "id_card"));
        var document = await ReadAsync(admin, courierId, "id_card");
        Assert.Equal("expiring", document["state"]!.GetValue<string>());
        Assert.Equal(0, document["daysLeft"]!.GetValue<int>());
        var clearance = await ClearanceAsync(admin, courierId);
        Assert.True(clearance["cleared"]!.GetValue<bool>());
        Assert.True(clearance["expiringSoon"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_car_courier_with_expired_insurance_is_blocked_and_says_why()
    {
        var (admin, courierId) = await CourierAsync("car");
        foreach (
            var (kind, until) in new[]
            {
                ("id_card", Today.AddYears(1)),
                ("driving_licence", Today.AddYears(1)),
                ("vehicle_insurance", Today.AddDays(-1)),
            }
        )
        {
            await SetAsync(admin, courierId, kind, $"{kind}-{courierId:N}"[..20], until);
            await ComplianceTests.VerifyAsync(admin, await ReadAsync(admin, courierId, kind));
        }
        var clearance = await ClearanceAsync(admin, courierId);
        Assert.False(clearance["cleared"]!.GetValue<bool>());
        Assert.Equal(
            "Vehicle insurance expired",
            Assert.Single(clearance["reasons"]!.AsArray())!.GetValue<string>()
        );
        // The expired one is behind us: the next expiry is a valid document's.
        Assert.Equal(
            Today.AddYears(1).ToString("yyyy-MM-dd"),
            clearance["nextExpiry"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task A_document_no_vehicle_needs_does_not_block()
    {
        var (admin, courierId) = await CourierAsync();
        await SetAsync(admin, courierId, "id_card", "IDOK00001", Today.AddYears(1));
        await ComplianceTests.VerifyAsync(admin, await ReadAsync(admin, courierId, "id_card"));
        await SetAsync(admin, courierId, "food_hygiene", "FOOD00001", Today.AddDays(-10));
        var clearance = await ClearanceAsync(admin, courierId);
        Assert.True(clearance["cleared"]!.GetValue<bool>());
        Assert.Empty(clearance["reasons"]!.AsArray());
    }

    [Fact]
    public async Task An_uncleared_courier_may_start_a_shift_but_cannot_be_assigned()
    {
        var shop = await DispatchTests.ShopAsync(api);
        var shipment = await DispatchTests.ShipmentAsync(
            api,
            shop.Admin,
            await DispatchTests.PackedOrderAsync(shop, (shop.Bread, 1))
        );
        var id = Guid.CreateVersion7();
        var created = await shop.Admin.PostAsync(ApiRoutes.Couriers, CarriersTests.Courier(id));
        await SetAsync(shop.Admin, id, "id_card", "OLD000001", Today.AddDays(-1));
        await ComplianceTests.VerifyAsync(shop.Admin, await ReadAsync(shop.Admin, id, "id_card"));
        // An expired document blocks assignment, not the shift.
        await shop.Admin.PostAsync(
            ApiRoutes.CourierShiftStart.With(id),
            new { rowVersion = created.RowVersion() }
        );
        await shop.Admin.PutAsync(
            ApiRoutes.CourierPosition.With(id),
            new { latitude = 50.0514m, longitude = 19.9447m }
        );
        var shipmentId = shipment["id"]!.GetValue<Guid>();
        Assert.DoesNotContain(
            await shop.Admin.ListAsync(ApiRoutes.ShipmentProposals.With(shipmentId)),
            p => p!["courierId"]!.GetValue<Guid>() == id
        );
        await shop.Admin.RefusedAsync(
            HttpMethod.Post,
            ApiRoutes.ShipmentAssign.With(shipmentId),
            new { courierId = id, rowVersion = shipment.RowVersion() },
            HttpStatusCode.Conflict,
            "The courier cannot take this shipment now"
        );
        Assert.Equal(
            "available",
            (await shop.Admin.GetObjectAsync(ApiRoutes.Courier.With(id)))[
                "shiftStatus"
            ]!.GetValue<string>()
        );
    }
}
