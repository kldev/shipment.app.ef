using System.Net;
using Shipment.Api.Endpoints;

namespace Shipment.Tests;

/// <summary>crm cases the first tests leave out: no-ops, uniqueness on edit, the default address, the actor, races.</summary>
[Trait("verify", "crm")]
[Collection(ApiCollection.Name)]
public sealed class CrmGapTests(ApiFixture api)
{
    private static readonly HttpMethod Post = HttpMethod.Post;
    private static readonly HttpMethod Put = HttpMethod.Put;

    private static string Code() => $"T{Guid.NewGuid():N}"[..10].ToUpperInvariant();

    private static object Partner(Guid id, string code) =>
        new
        {
            id,
            code,
            name = "Gap Grocer",
            kind = "grocery",
            commissionBps = 1500,
        };

    private static object PartnerChange(string code, uint rowVersion, string name = "Gap Grocer") =>
        new
        {
            code,
            name,
            kind = "grocery",
            commissionBps = 1500,
            status = "active",
            rowVersion,
        };

    private static object Address(
        Guid id,
        string label = "Home",
        bool isDefault = false,
        decimal latitude = 50.0700m
    ) =>
        new
        {
            id,
            label,
            addressLine = "ul. Długa 15",
            city = "Kraków",
            postcode = "31-147",
            latitude,
            longitude = 19.9420m,
            isDefault,
        };

    private static object AddressChange(
        uint rowVersion,
        string label = "Home",
        bool isDefault = false
    ) =>
        new
        {
            label,
            addressLine = "ul. Długa 15",
            city = "Kraków",
            postcode = "31-147",
            latitude = 50.0700m,
            longitude = 19.9420m,
            isDefault,
            rowVersion,
        };

    private async Task<(HttpClient Admin, Guid CustomerId)> CustomerAsync()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var id = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.Customers,
            new
            {
                id,
                displayName = "Gap Customer",
                email = $"{id:N}@example.com",
            }
        );
        return (admin, id);
    }

    private static async Task<uint> AddressVersionAsync(
        HttpClient admin,
        Guid customerId,
        Guid addressId
    ) =>
        Assert
            .Single(
                await admin.ListAsync(ApiRoutes.CustomerAddresses.With(customerId)),
                a => a!["id"]!.GetValue<Guid>() == addressId
            )!
            .AsObject()
            .RowVersion();

    private static async Task<List<Guid>> DefaultsAsync(HttpClient admin, Guid customerId) =>
        (await admin.ListAsync(ApiRoutes.CustomerAddresses.With(customerId)))
            .Where(a => a!["isDefault"]!.GetValue<bool>())
            .Select(a => a!["id"]!.GetValue<Guid>())
            .ToList();

    [Fact]
    public async Task A_customer_address_is_created_with_201_and_its_location()
    {
        var (admin, customerId) = await CustomerAsync();
        var id = Guid.CreateVersion7();
        var response = await admin.SendRawAsync(
            Post,
            ApiRoutes.CustomerAddresses.With(customerId),
            Address(id)
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(ApiRoutes.CustomerAddress.With(id), response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task An_address_is_created_idempotently_by_its_id()
    {
        var (admin, customerId) = await CustomerAsync();
        var route = ApiRoutes.CustomerAddresses.With(customerId);
        var id = Guid.CreateVersion7();
        var created = await admin.PostAsync(route, Address(id));
        // A retried request: the same row, nothing new (the first address took the default flag - still the same).
        var again = await admin.PostAsync(route, Address(id));
        Assert.Equal(created.RowVersion(), again.RowVersion());
        Assert.Single(await admin.ListAsync(route));
        await admin.RefusedAsync(
            Post,
            route,
            Address(id, label: "Office"),
            HttpStatusCode.Conflict,
            "already used"
        );
        // The same id under another customer is someone else's address.
        var (_, otherCustomer) = await CustomerAsync();
        await admin.RefusedAsync(
            Post,
            ApiRoutes.CustomerAddresses.With(otherCustomer),
            Address(id),
            HttpStatusCode.Conflict,
            "already used"
        );
    }

    [Fact]
    public async Task A_retry_with_more_decimals_than_stored_is_the_same_address()
    {
        // Coordinates are numeric(9,6): rounded to 6 places before comparing.
        var (admin, customerId) = await CustomerAsync();
        var route = ApiRoutes.CustomerAddresses.With(customerId);
        var id = Guid.CreateVersion7();
        var created = await admin.PostAsync(route, Address(id, latitude: 50.07001234m));
        var again = await admin.PostAsync(route, Address(id, latitude: 50.07001234m));
        Assert.Equal(created.RowVersion(), again.RowVersion());
        Assert.Equal(
            50.070012m,
            Assert.Single(await admin.ListAsync(route))!["latitude"]!.GetValue<decimal>()
        );
    }

    [Fact]
    public async Task Editing_addresses_moves_the_default_flag_but_never_unsets_it()
    {
        var (admin, customerId) = await CustomerAsync();
        var route = ApiRoutes.CustomerAddresses.With(customerId);
        var home = Guid.CreateVersion7();
        var office = Guid.CreateVersion7();
        await admin.PostAsync(route, Address(home));
        await admin.PostAsync(route, Address(office, label: "Office"));
        Assert.Equal([home], await DefaultsAsync(admin, customerId));

        // The same values: a no-op.
        var officeVersion = await AddressVersionAsync(admin, customerId, office);
        var same = await admin.PutAsync(
            ApiRoutes.CustomerAddress.With(office),
            AddressChange(officeVersion, label: "Office")
        );
        Assert.Equal(officeVersion, same.RowVersion());

        // Making the office the default takes the flag from home.
        var moved = await admin.PutAsync(
            ApiRoutes.CustomerAddress.With(office),
            AddressChange(officeVersion, label: "Office", isDefault: true)
        );
        Assert.NotEqual(officeVersion, moved.RowVersion());
        Assert.Equal([office], await DefaultsAsync(admin, customerId));

        // "Not default" on the default: it stays the default (another address must take it).
        await admin.PutAsync(
            ApiRoutes.CustomerAddress.With(office),
            AddressChange(moved.RowVersion(), label: "Office (2)", isDefault: false)
        );
        Assert.Equal([office], await DefaultsAsync(admin, customerId));

        // The office's first version is stale.
        await admin.RefusedAsync(
            Put,
            ApiRoutes.CustomerAddress.With(office),
            AddressChange(officeVersion, label: "Office"),
            HttpStatusCode.Conflict,
            "was changed by someone else"
        );
        await admin.RefusedAsync(
            Put,
            ApiRoutes.CustomerAddress.With(home),
            AddressChange(await AddressVersionAsync(admin, customerId, home), label: " "),
            HttpStatusCode.BadRequest,
            "Give the address a label"
        );
    }

    [Fact]
    public async Task Two_first_addresses_at_once_leave_exactly_one_default()
    {
        var (admin, customerId) = await CustomerAsync();
        var route = ApiRoutes.CustomerAddresses.With(customerId);
        var statuses = await GapCalls.RaceAsync(
            () => admin.SendRawAsync(Post, route, Address(Guid.CreateVersion7())),
            () => admin.SendRawAsync(Post, route, Address(Guid.CreateVersion7(), "Office"))
        );
        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.All(
            statuses,
            s => Assert.True(s is HttpStatusCode.Created or HttpStatusCode.Conflict, $"{s}")
        );
        Assert.Single(await DefaultsAsync(admin, customerId));
    }

    [Fact]
    public async Task Two_new_defaults_at_once_leave_exactly_one_default()
    {
        var (admin, customerId) = await CustomerAsync();
        var route = ApiRoutes.CustomerAddresses.With(customerId);
        await admin.PostAsync(route, Address(Guid.CreateVersion7()));
        var statuses = await GapCalls.RaceAsync(
            () =>
                admin.SendRawAsync(
                    Post,
                    route,
                    Address(Guid.CreateVersion7(), "Office", isDefault: true)
                ),
            () =>
                admin.SendRawAsync(
                    Post,
                    route,
                    Address(Guid.CreateVersion7(), "Parents", isDefault: true)
                )
        );
        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.All(
            statuses,
            s => Assert.True(s is HttpStatusCode.Created or HttpStatusCode.Conflict, $"{s}")
        );
        Assert.Single(await DefaultsAsync(admin, customerId));
    }

    [Fact]
    public async Task Unchanged_pickup_points_and_customers_keep_their_version()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var partnerId = Guid.CreateVersion7();
        await admin.PostAsync(ApiRoutes.Partners, Partner(partnerId, Code()));
        var pointId = Guid.CreateVersion7();
        var point = await admin.PostAsync(
            ApiRoutes.PickupPoints,
            new
            {
                id = pointId,
                partnerId,
                name = "Old Town",
                addressLine = "Rynek Główny 1",
                city = "Kraków",
                postcode = "31-042",
                latitude = 50.0617m,
                longitude = 19.9373m,
                opensMinute = 480,
                closesMinute = 1320,
            }
        );
        var same = await admin.PutAsync(
            ApiRoutes.PickupPoint.With(pointId),
            new
            {
                name = "Old Town",
                addressLine = "Rynek Główny 1",
                city = "Kraków",
                postcode = "31-042",
                latitude = 50.0617m,
                longitude = 19.9373m,
                opensMinute = 480,
                closesMinute = 1320,
                status = "active",
                rowVersion = point.RowVersion(),
            }
        );
        Assert.Equal(point.RowVersion(), same.RowVersion());

        var customerId = Guid.CreateVersion7();
        var customer = await admin.PostAsync(
            ApiRoutes.Customers,
            new
            {
                id = customerId,
                displayName = "Same Customer",
                email = $"{customerId:N}@example.com",
                phone = "+48 600 100 200",
            }
        );
        var unchanged = await admin.PutAsync(
            ApiRoutes.Customer.With(customerId),
            new
            {
                displayName = "Same Customer",
                email = $"{customerId:N}@example.com",
                phone = "+48 600 100 200",
                rowVersion = customer.RowVersion(),
            }
        );
        Assert.Equal(customer.RowVersion(), unchanged.RowVersion());
    }

    [Fact]
    public async Task Codes_and_emails_stay_unique_on_edit_and_regardless_of_case()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var takenCode = Code();
        await admin.PostAsync(ApiRoutes.Partners, Partner(Guid.CreateVersion7(), takenCode));
        var partnerId = Guid.CreateVersion7();
        var partner = await admin.PostAsync(ApiRoutes.Partners, Partner(partnerId, Code()));
        await admin.RefusedAsync(
            Put,
            ApiRoutes.Partner.With(partnerId),
            PartnerChange(takenCode, partner.RowVersion()),
            HttpStatusCode.Conflict,
            "Another partner already uses this code"
        );

        var first = Guid.CreateVersion7();
        var email = $"{first:N}@example.com";
        await admin.PostAsync(
            ApiRoutes.Customers,
            new
            {
                id = first,
                displayName = "First",
                email = email.ToUpperInvariant(),
            }
        );
        // E-mails compare without case.
        Assert.Equal(
            email,
            (await admin.GetObjectAsync(ApiRoutes.Customer.With(first)))[
                "email"
            ]!.GetValue<string>()
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Customers,
            new
            {
                id = Guid.CreateVersion7(),
                displayName = "Second",
                email,
            },
            HttpStatusCode.Conflict,
            "Another customer already uses this e-mail"
        );
        var second = Guid.CreateVersion7();
        var created = await admin.PostAsync(
            ApiRoutes.Customers,
            new
            {
                id = second,
                displayName = "Second",
                email = $"{second:N}@example.com",
            }
        );
        await admin.RefusedAsync(
            Put,
            ApiRoutes.Customer.With(second),
            new
            {
                displayName = "Second",
                email,
                rowVersion = created.RowVersion(),
            },
            HttpStatusCode.Conflict,
            "Another customer already uses this e-mail"
        );
    }

    [Fact]
    public async Task A_write_through_the_api_stamps_the_signed_in_user()
    {
        var username = await api.AddAdminAsync($"gap.{Guid.NewGuid():N}"[..20], "Gap Editor");
        var userId = await api.ScalarAsync<Guid>(
            $"SELECT id FROM identity.users WHERE username = '{username}'"
        );
        var admin = await api.AsAsync(ApiFixture.Admin);
        var editor = await api.AsAsync(username);

        var partnerId = Guid.CreateVersion7();
        var code = Code();
        var created = await admin.PostAsync(ApiRoutes.Partners, Partner(partnerId, code));
        Assert.Equal(
            ApiFixture.AdminId.ToString(),
            await api.ScalarAsync<string>(
                $"SELECT created_by::text FROM crm.partners WHERE id = '{partnerId}'"
            )
        );
        Assert.Equal(
            ApiFixture.AdminId.ToString(),
            await api.ScalarAsync<string>(
                $"SELECT modified_by::text FROM crm.partners WHERE id = '{partnerId}'"
            )
        );

        await editor.PutAsync(
            ApiRoutes.Partner.With(partnerId),
            PartnerChange(code, created.RowVersion(), name: "Renamed by the editor")
        );
        Assert.Equal(
            userId.ToString(),
            await api.ScalarAsync<string>(
                $"SELECT modified_by::text FROM crm.partners WHERE id = '{partnerId}'"
            )
        );
        Assert.Equal(
            ApiFixture.AdminId.ToString(),
            await api.ScalarAsync<string>(
                $"SELECT created_by::text FROM crm.partners WHERE id = '{partnerId}'"
            )
        );
        Assert.True(
            await api.ScalarAsync<bool>(
                $"SELECT modified_at > created_at FROM crm.partners WHERE id = '{partnerId}'"
            )
        );
    }

    [Fact]
    public async Task Two_edits_with_the_same_version_at_once_one_wins_one_is_a_conflict()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var partnerId = Guid.CreateVersion7();
        var code = Code();
        var created = await admin.PostAsync(ApiRoutes.Partners, Partner(partnerId, code));
        var statuses = await GapCalls.RaceAsync(
            () =>
                admin.SendRawAsync(
                    Put,
                    ApiRoutes.Partner.With(partnerId),
                    PartnerChange(code, created.RowVersion(), name: "Edit A")
                ),
            () =>
                admin.SendRawAsync(
                    Put,
                    ApiRoutes.Partner.With(partnerId),
                    PartnerChange(code, created.RowVersion(), name: "Edit B")
                )
        );
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);
        Assert.Contains(
            (await admin.GetObjectAsync(ApiRoutes.Partner.With(partnerId)))[
                "name"
            ]!.GetValue<string>(),
            new[] { "Edit A", "Edit B" }
        );
    }
}
