using System.Net;
using Shipment.Api.Endpoints;

namespace Shipment.Tests;

/// <summary>Module crm: partners, pickup points, customers and their delivery addresses (spec 001).</summary>
[Trait("verify", "crm")]
[Collection(ApiCollection.Name)]
public sealed class CrmTests(ApiFixture api)
{
    private static readonly HttpMethod Post = HttpMethod.Post;
    private static readonly HttpMethod Put = HttpMethod.Put;

    private static string Code() => $"T{Guid.NewGuid():N}"[..10].ToUpperInvariant();

    private static object Partner(Guid id, string code, string name = "Test Grocer") =>
        new
        {
            id,
            code,
            name,
            kind = "grocery",
            commissionBps = 1500,
        };

    private static object Point(Guid id, Guid partnerId, decimal latitude = 50.0617m) =>
        new
        {
            id,
            partnerId,
            name = "Old Town",
            addressLine = "Rynek Główny 1",
            city = "Kraków",
            postcode = "31-042",
            latitude,
            longitude = 19.9373m,
            opensMinute = 480,
            closesMinute = 1320,
        };

    private static object PointChange(
        uint rowVersion,
        string status = "active",
        int opensMinute = 480,
        int closesMinute = 1320
    ) =>
        new
        {
            name = "Old Town",
            addressLine = "Rynek Główny 1",
            city = "Kraków",
            postcode = "31-042",
            latitude = 50.0617m,
            longitude = 19.9373m,
            opensMinute,
            closesMinute,
            status,
            rowVersion,
        };

    [Fact]
    public async Task A_partner_is_created_idempotently_by_its_id_and_edited_with_its_row_version()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var id = Guid.CreateVersion7();
        var code = Code();

        var created = await admin.PostAsync(ApiRoutes.Partners, Partner(id, code));
        // A retried request: same id, same values - the same row, nothing new.
        var again = await admin.PostAsync(ApiRoutes.Partners, Partner(id, code));
        Assert.Equal(created.RowVersion(), again.RowVersion());
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Partners,
            Partner(id, code, "Someone Else"),
            HttpStatusCode.Conflict,
            "already used by another partner"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Partners,
            Partner(Guid.CreateVersion7(), code),
            HttpStatusCode.Conflict,
            "Another partner already uses this code"
        );

        var update = new
        {
            code,
            name = "Test Grocer Plus",
            kind = "grocery",
            commissionBps = 1200,
            status = "paused",
            rowVersion = created.RowVersion(),
        };
        var edited = await admin.PutAsync(ApiRoutes.Partner.With(id), update);
        Assert.NotEqual(created.RowVersion(), edited.RowVersion());
        // The same values again: a no-op, the version stays.
        var same = await admin.PutAsync(
            ApiRoutes.Partner.With(id),
            update with
            {
                rowVersion = edited.RowVersion(),
            }
        );
        Assert.Equal(edited.RowVersion(), same.RowVersion());
        // The first version is stale now.
        await admin.RefusedAsync(
            Put,
            ApiRoutes.Partner.With(id),
            update,
            HttpStatusCode.Conflict,
            "was changed by someone else"
        );

        var read = await admin.GetObjectAsync(ApiRoutes.Partner.With(id));
        Assert.Equal("paused", read["status"]!.GetValue<string>());
        Assert.Equal(1200, read["commissionBps"]!.GetValue<int>());
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await admin.GetAsync(
                    ApiRoutes.Partner.With(Guid.CreateVersion7()),
                    TestContext.Current.CancellationToken
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task Partner_rules_are_refused_with_their_messages()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var cases = new (object Body, string Message)[]
        {
            (Partner(Guid.CreateVersion7(), Code(), " "), "Partner name is required"),
            (Partner(Guid.CreateVersion7(), "x"), "Partner code must be 2 to 12 letters or digits"),
            (
                new
                {
                    id = Guid.CreateVersion7(),
                    code = Code(),
                    name = "N",
                    kind = "shop",
                    commissionBps = 6000,
                },
                "Commission must be between 0 and 50 %"
            ),
            // A missing id binds as the empty id.
            (
                new
                {
                    code = Code(),
                    name = "No id",
                    kind = "shop",
                    commissionBps = 100,
                },
                "Partner id is required"
            ),
        };
        foreach (var (body, message) in cases)
            await admin.RefusedAsync(
                Post,
                ApiRoutes.Partners,
                body,
                HttpStatusCode.BadRequest,
                message
            );

        // An unknown kind does not bind (the enum names are the only values).
        await admin.NotBoundAsync(
            Post,
            ApiRoutes.Partners,
            new
            {
                id = Guid.CreateVersion7(),
                code = Code(),
                name = "N",
                kind = "bakery",
                commissionBps = 100,
            }
        );

        await admin.RefusedAsync(
            Put,
            ApiRoutes.Partner.With(Guid.CreateVersion7()),
            new
            {
                code = Code(),
                name = "N",
                kind = "shop",
                commissionBps = 100,
                status = "active",
                rowVersion = 1,
            },
            HttpStatusCode.NotFound,
            "Partner not found"
        );
    }

    [Fact]
    public async Task A_pickup_point_belongs_to_a_partner_and_keeps_its_place_and_hours()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var partnerId = Guid.CreateVersion7();
        await admin.PostAsync(ApiRoutes.Partners, Partner(partnerId, Code()));

        var id = Guid.CreateVersion7();
        var created = await admin.PostAsync(ApiRoutes.PickupPoints, Point(id, partnerId));
        Assert.Equal(
            created.RowVersion(),
            (await admin.PostAsync(ApiRoutes.PickupPoints, Point(id, partnerId))).RowVersion()
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.PickupPoints,
            Point(id, partnerId, latitude: 50.1m),
            HttpStatusCode.Conflict,
            "already used by another pickup point"
        );

        var points = await admin.ListAsync($"{ApiRoutes.PickupPoints}?partnerId={partnerId}");
        var point = Assert.Single(points);
        Assert.Equal(50.0617m, point!["latitude"]!.GetValue<decimal>());
        Assert.Equal("Test Grocer", point["partnerName"]!.GetValue<string>());

        await admin.RefusedAsync(
            Post,
            ApiRoutes.PickupPoints,
            Point(Guid.CreateVersion7(), Guid.CreateVersion7()),
            HttpStatusCode.NotFound,
            "Partner not found"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.PickupPoints,
            Point(Guid.CreateVersion7(), partnerId, latitude: 91m),
            HttpStatusCode.BadRequest,
            "Pick the place on the map"
        );
        await admin.RefusedAsync(
            Put,
            ApiRoutes.PickupPoint.With(id),
            PointChange(created.RowVersion(), opensMinute: 1320, closesMinute: 480),
            HttpStatusCode.BadRequest,
            "it must open before it closes"
        );

        var closed = await admin.PutAsync(
            ApiRoutes.PickupPoint.With(id),
            PointChange(created.RowVersion(), status: "closed")
        );
        Assert.NotEqual(created.RowVersion(), closed.RowVersion());
        await admin.RefusedAsync(
            Put,
            ApiRoutes.PickupPoint.With(id),
            PointChange(created.RowVersion()),
            HttpStatusCode.Conflict,
            "was changed by someone else"
        );
        var read = await admin.GetObjectAsync(ApiRoutes.PickupPoint.With(id));
        Assert.Equal("closed", read["status"]!.GetValue<string>());
        Assert.False(read["isOpenNow"]!.GetValue<bool>());
        Assert.Equal(
            0,
            (await admin.GetObjectAsync(ApiRoutes.Partner.With(partnerId)))[
                "activePickupPoints"
            ]!.GetValue<int>()
        );
    }

    [Fact]
    public async Task A_customer_has_a_unique_email_and_one_default_address()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var id = Guid.CreateVersion7();
        var email = $"{id:N}@example.com";
        var customer = new
        {
            id,
            displayName = "Ola Test",
            email,
            phone = "+48 600 100 200",
        };
        var created = await admin.PostAsync(ApiRoutes.Customers, customer);
        Assert.Equal(
            created.RowVersion(),
            (await admin.PostAsync(ApiRoutes.Customers, customer)).RowVersion()
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Customers,
            customer with
            {
                id = Guid.CreateVersion7(),
            },
            HttpStatusCode.Conflict,
            "Another customer already uses this e-mail"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Customers,
            customer with
            {
                id = Guid.CreateVersion7(),
                email = "not-an-email",
            },
            HttpStatusCode.BadRequest,
            "Enter a valid e-mail address"
        );

        var edited = await admin.PutAsync(
            ApiRoutes.Customer.With(id),
            new
            {
                displayName = "Ola Test-Nowak",
                email,
                phone = "+48 600 100 200",
                rowVersion = created.RowVersion(),
            }
        );
        Assert.NotEqual(created.RowVersion(), edited.RowVersion());
        await admin.RefusedAsync(
            Put,
            ApiRoutes.Customer.With(id),
            new
            {
                displayName = "Ola Test",
                email,
                rowVersion = created.RowVersion(),
            },
            HttpStatusCode.Conflict,
            "was changed by someone else"
        );

        var home = Guid.CreateVersion7();
        var office = Guid.CreateVersion7();
        object Address(Guid addressId, string label, bool isDefault) =>
            new
            {
                id = addressId,
                label,
                addressLine = "ul. Długa 15",
                city = "Kraków",
                postcode = "31-147",
                latitude = 50.0700m,
                longitude = 19.9420m,
                isDefault,
            };
        var addressesRoute = ApiRoutes.CustomerAddresses.With(id);
        // The first address becomes the default even when not asked.
        await admin.PostAsync(addressesRoute, Address(home, "Home", false));
        Assert.True(
            Assert.Single(await admin.ListAsync(addressesRoute))!["isDefault"]!.GetValue<bool>()
        );

        // A new default takes the flag from the old one.
        await admin.PostAsync(addressesRoute, Address(office, "Office", true));
        var addresses = await admin.ListAsync(addressesRoute);
        Assert.Equal(2, addresses.Count);
        var only = Assert.Single(addresses, a => a!["isDefault"]!.GetValue<bool>());
        Assert.Equal(office, only!["id"]!.GetValue<Guid>());
        Assert.Equal(
            office,
            Assert.Single(await admin.ListAsync($"{addressesRoute}?isDefault=true"))![
                "id"
            ]!.GetValue<Guid>()
        );

        await admin.RefusedAsync(
            Post,
            addressesRoute,
            Address(Guid.CreateVersion7(), " ", false),
            HttpStatusCode.BadRequest,
            "Give the address a label"
        );
        var read = await admin.GetObjectAsync(ApiRoutes.Customer.With(id));
        Assert.Equal(2, read["addressCount"]!.GetValue<int>());
        Assert.Equal("Kraków", read["defaultCity"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_remaining_crm_rules_are_refused_with_their_messages()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var partnerId = Guid.CreateVersion7();
        var partner = await admin.PostAsync(ApiRoutes.Partners, Partner(partnerId, Code()));

        // An unknown status does not bind.
        await admin.NotBoundAsync(
            Put,
            ApiRoutes.Partner.With(partnerId),
            new
            {
                code = Code(),
                name = "N",
                kind = "shop",
                commissionBps = 100,
                status = "gone",
                rowVersion = partner.RowVersion(),
            }
        );

        object NewPoint(string name, string addressLine) =>
            new
            {
                id = Guid.CreateVersion7(),
                partnerId,
                name,
                addressLine,
                city = "Kraków",
                postcode = "31-000",
                latitude = 50m,
                longitude = 19m,
                opensMinute = 0,
                closesMinute = 60,
            };
        await admin.RefusedAsync(
            Post,
            ApiRoutes.PickupPoints,
            NewPoint(" ", "A 1"),
            HttpStatusCode.BadRequest,
            "Pickup point name is required"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.PickupPoints,
            NewPoint("N", " "),
            HttpStatusCode.BadRequest,
            "Street, city and postcode are required"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.PickupPoints,
            new
            {
                partnerId,
                name = "No id",
                addressLine = "A 1",
                city = "Kraków",
                postcode = "31-000",
                latitude = 50m,
                longitude = 19m,
                opensMinute = 0,
                closesMinute = 60,
            },
            HttpStatusCode.BadRequest,
            "Pickup point id is required"
        );
        var pointId = Guid.CreateVersion7();
        var created = await admin.PostAsync(ApiRoutes.PickupPoints, Point(pointId, partnerId));
        await admin.NotBoundAsync(
            Put,
            ApiRoutes.PickupPoint.With(pointId),
            PointChange(created.RowVersion(), status: "demolished")
        );

        var customerId = Guid.CreateVersion7();
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Customers,
            new
            {
                id = customerId,
                displayName = " ",
                email = $"{customerId:N}@example.com",
            },
            HttpStatusCode.BadRequest,
            "Customer name is required"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Customers,
            new
            {
                id = customerId,
                displayName = "Phone Test",
                email = $"{customerId:N}@example.com",
                phone = "call me maybe",
            },
            HttpStatusCode.BadRequest,
            "Phone may contain digits"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Customers,
            new { displayName = "No id", email = $"{customerId:N}@example.com" },
            HttpStatusCode.BadRequest,
            "Customer id is required"
        );
        await admin.RefusedAsync(
            Put,
            ApiRoutes.Customer.With(Guid.CreateVersion7()),
            new
            {
                displayName = "Nobody",
                email = "nobody@example.com",
                rowVersion = 1,
            },
            HttpStatusCode.NotFound,
            "Customer not found"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.CustomerAddresses.With(Guid.CreateVersion7()),
            new
            {
                id = Guid.CreateVersion7(),
                label = "Home",
                addressLine = "A 1",
                city = "Kraków",
                postcode = "31-000",
                latitude = 50m,
                longitude = 19m,
            },
            HttpStatusCode.NotFound,
            "Customer not found"
        );
        await admin.RefusedAsync(
            Put,
            ApiRoutes.CustomerAddress.With(Guid.CreateVersion7()),
            new
            {
                label = "Home",
                addressLine = "A 1",
                city = "Kraków",
                postcode = "31-000",
                latitude = 50m,
                longitude = 19m,
                rowVersion = 1,
            },
            HttpStatusCode.NotFound,
            "Address not found"
        );
        await admin.RefusedAsync(
            Put,
            ApiRoutes.PickupPoint.With(Guid.CreateVersion7()),
            new
            {
                name = "N",
                addressLine = "A 1",
                city = "Kraków",
                postcode = "31-000",
                latitude = 50m,
                longitude = 19m,
                opensMinute = 0,
                closesMinute = 60,
                status = "active",
                rowVersion = 1,
            },
            HttpStatusCode.NotFound,
            "Pickup point not found"
        );
    }
}
