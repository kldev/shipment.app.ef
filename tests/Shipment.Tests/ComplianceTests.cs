using System.Net;
using System.Text.Json.Nodes;
using Shipment.Api.Endpoints;
using Shipment.Domain.Common;

namespace Shipment.Tests;

/// <summary>Module compliance: courier documents, clearance and the age check (spec 002).</summary>
[Trait("verify", "compliance")]
[Collection(ApiCollection.Name)]
public sealed class ComplianceTests(ApiFixture api)
{
    private static readonly HttpMethod Post = HttpMethod.Post;

    // Documents count by the operator's day (Europe/Warsaw).
    private static DateOnly Today => OperatorTime.Today(DateTimeOffset.UtcNow);
    private static DateOnly NextYear => Today.AddYears(1);

    private static async Task<JsonObject> ClearanceAsync(HttpClient admin, Guid courierId) =>
        Assert
            .Single(await admin.ListAsync($"{ApiRoutes.CourierClearance}?courierId={courierId}"))!
            .AsObject();

    internal static async Task<JsonObject> DocumentAsync(
        HttpClient admin,
        Guid courierId,
        string kind,
        DateOnly validUntil
    )
    {
        await admin.PostAsync(
            ApiRoutes.CourierDocuments,
            new
            {
                id = Guid.CreateVersion7(),
                courierId,
                kind,
                number = $"{kind}-{courierId:N}"[..20],
                validUntil = validUntil.ToString("yyyy-MM-dd"),
            }
        );
        return Assert
            .Single(
                await admin.ListAsync(
                    $"{ApiRoutes.CourierDocuments}?courierId={courierId}&kind={kind}"
                )
            )!
            .AsObject();
    }

    internal static Task VerifyAsync(HttpClient admin, JsonObject document) =>
        admin.PostAsync(
            ApiRoutes.CourierDocumentVerify.With(document["id"]!.GetValue<Guid>()),
            new { rowVersion = document.RowVersion() }
        );

    [Fact]
    public async Task A_car_courier_is_cleared_only_with_a_verified_id_licence_and_insurance()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var courierId = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.Couriers,
            CarriersTests.Courier(courierId, "car", "KR 5555")
        );

        var none = await ClearanceAsync(admin, courierId);
        Assert.False(none["cleared"]!.GetValue<bool>());
        Assert.Equal(
            ["Driving licence missing", "ID card missing", "Vehicle insurance missing"],
            none["reasons"]!.AsArray().Select(r => r!.GetValue<string>()).Order()
        );

        foreach (var kind in new[] { "id_card", "driving_licence", "vehicle_insurance" })
        {
            var document = await DocumentAsync(admin, courierId, kind, NextYear);
            Assert.Equal("unverified", document["state"]!.GetValue<string>());
            await VerifyAsync(admin, document);
            // Verified already: a no-op, the version stays.
            var verified = Assert
                .Single(
                    await admin.ListAsync(
                        $"{ApiRoutes.CourierDocuments}?courierId={courierId}&kind={kind}"
                    )
                )!
                .AsObject();
            Assert.Equal(
                verified.RowVersion(),
                (
                    await admin.PostAsync(
                        ApiRoutes.CourierDocumentVerify.With(verified["id"]!.GetValue<Guid>()),
                        new { rowVersion = verified.RowVersion() }
                    )
                ).RowVersion()
            );
        }
        Assert.True((await ClearanceAsync(admin, courierId))["cleared"]!.GetValue<bool>());

        // Renewed with another date: verify again; the document keeps its id.
        var before = Assert.Single(
            await admin.ListAsync(
                $"{ApiRoutes.CourierDocuments}?courierId={courierId}&kind=vehicle_insurance"
            )
        )!;
        var renewed = await DocumentAsync(
            admin,
            courierId,
            "vehicle_insurance",
            NextYear.AddDays(30)
        );
        Assert.Equal(before["id"]!.GetValue<Guid>(), renewed["id"]!.GetValue<Guid>());
        Assert.Equal("unverified", renewed["state"]!.GetValue<string>());
        var after = await ClearanceAsync(admin, courierId);
        Assert.False(after["cleared"]!.GetValue<bool>());
        Assert.Equal(
            "Vehicle insurance not verified",
            Assert.Single(after["reasons"]!.AsArray())!.GetValue<string>()
        );
    }

    [Fact]
    public async Task A_bike_courier_needs_only_an_id_and_an_expired_one_blocks()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var courierId = Guid.CreateVersion7();
        await admin.PostAsync(ApiRoutes.Couriers, CarriersTests.Courier(courierId));

        var expired = await DocumentAsync(admin, courierId, "id_card", Today.AddDays(-2));
        Assert.Equal("expired", expired["state"]!.GetValue<string>());
        await VerifyAsync(admin, expired);
        var clearance = await ClearanceAsync(admin, courierId);
        Assert.False(clearance["cleared"]!.GetValue<bool>());
        Assert.Equal(
            "ID card expired",
            Assert.Single(clearance["reasons"]!.AsArray())!.GetValue<string>()
        );

        var soon = await DocumentAsync(admin, courierId, "id_card", Today.AddDays(10));
        await VerifyAsync(admin, soon);
        clearance = await ClearanceAsync(admin, courierId);
        Assert.True(clearance["cleared"]!.GetValue<bool>());
        Assert.True(clearance["expiringSoon"]!.GetValue<bool>());
        Assert.Equal(
            "expiring",
            Assert.Single(
                await admin.ListAsync($"{ApiRoutes.CourierDocuments}?courierId={courierId}")
            )!["state"]!.GetValue<string>()
        );
        Assert.Single(
            await admin.ListAsync(
                $"{ApiRoutes.CourierDocuments}?courierId={courierId}&state=expiring"
            )
        );
    }

    [Fact]
    public async Task Document_rules_are_refused_with_their_messages()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var courierId = Guid.CreateVersion7();
        await admin.PostAsync(ApiRoutes.Couriers, CarriersTests.Courier(courierId));
        var date = NextYear.ToString("yyyy-MM-dd");
        object Body(Guid id, Guid courier, object kind, string number) =>
            new
            {
                id,
                courierId = courier,
                kind,
                number,
                validUntil = date,
            };

        // An unknown kind name does not bind; a number that is no kind is the service's refusal.
        await admin.NotBoundAsync(
            Post,
            ApiRoutes.CourierDocuments,
            Body(Guid.CreateVersion7(), courierId, "passport", "X1")
        );
        // Enum values as numbers do not bind either (allowIntegerValues: false).
        await admin.NotBoundAsync(
            Post,
            ApiRoutes.CourierDocuments,
            Body(Guid.CreateVersion7(), courierId, 9, "X1")
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.CourierDocuments,
            Body(Guid.CreateVersion7(), courierId, "id_card", " "),
            HttpStatusCode.BadRequest,
            "Document number is required"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.CourierDocuments,
            new
            {
                id = Guid.CreateVersion7(),
                courierId,
                kind = "id_card",
                number = "X1",
            },
            HttpStatusCode.BadRequest,
            "Say until when the document is valid"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.CourierDocuments,
            Body(Guid.Empty, courierId, "id_card", "X1"),
            HttpStatusCode.BadRequest,
            "Document id is required"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.CourierDocuments,
            Body(Guid.CreateVersion7(), Guid.CreateVersion7(), "id_card", "X1"),
            HttpStatusCode.NotFound,
            "Courier not found"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.CourierDocumentVerify.With(Guid.CreateVersion7()),
            new { rowVersion = 1 },
            HttpStatusCode.NotFound,
            "Document not found"
        );

        var document = await DocumentAsync(admin, courierId, "id_card", NextYear);
        var documentId = document["id"]!.GetValue<Guid>();
        await admin.RefusedAsync(
            Post,
            ApiRoutes.CourierDocuments,
            Body(documentId, Guid.CreateVersion7(), "id_card", "X1"),
            HttpStatusCode.NotFound,
            "Courier not found"
        );
        var other = Guid.CreateVersion7();
        await admin.PostAsync(ApiRoutes.Couriers, CarriersTests.Courier(other));
        await admin.RefusedAsync(
            Post,
            ApiRoutes.CourierDocuments,
            Body(documentId, other, "id_card", "X1"),
            HttpStatusCode.Conflict,
            "already used by another document"
        );
        await admin.RefusedAsync(
            Post,
            ApiRoutes.CourierDocumentVerify.With(documentId),
            new { rowVersion = document.RowVersion() + 1 },
            HttpStatusCode.Conflict,
            "was changed by someone else"
        );
    }

    [Fact]
    public async Task The_age_check_names_the_restricted_products()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var partnerId = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.Partners,
            new
            {
                id = partnerId,
                code = $"A{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
                name = "Age Test",
                kind = "grocery",
                commissionBps = 100,
            }
        );
        async Task ProductAsync(string sku, string name, bool ageRestricted, List<Guid> ids)
        {
            var id = Guid.CreateVersion7();
            ids.Add(id);
            await admin.PostAsync(
                ApiRoutes.Products,
                new
                {
                    id,
                    partnerId,
                    sku,
                    name,
                    category = "drinks",
                    priceMinor = 500,
                    weightG = 500,
                    sizeClass = "S",
                    ageRestricted,
                }
            );
        }
        static string Query(IEnumerable<Guid> ids) =>
            $"{ApiRoutes.AgeCheck}?{string.Join("&", ids.Select(id => $"productIds={id}"))}";

        var basket = new List<Guid>();
        await ProductAsync("JUICE", "Juice", false, basket);
        var juiceOnly = basket.ToList();
        await ProductAsync("WINE", "Wine", true, basket);

        var plain = await admin.GetObjectAsync(Query(juiceOnly));
        Assert.False(plain["idCheckRequired"]!.GetValue<bool>());
        Assert.Empty(plain["restrictedProducts"]!.AsArray());
        var mixed = await admin.GetObjectAsync(Query(basket));
        Assert.True(mixed["idCheckRequired"]!.GetValue<bool>());
        Assert.Equal(
            "Wine",
            Assert.Single(mixed["restrictedProducts"]!.AsArray())!.GetValue<string>()
        );

        var refused = await admin.GetAsync(
            $"{ApiRoutes.AgeCheck}?productIds=not-an-id",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }
}
