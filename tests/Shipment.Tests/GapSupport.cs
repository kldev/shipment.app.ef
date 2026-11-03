using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shipment.Api.Endpoints;

namespace Shipment.Tests;

/// <summary>Raw calls for the gap tests: the status of a write without asserting it, for races and headers.</summary>
internal static class GapCalls
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> SendRawAsync(
        this HttpClient client,
        HttpMethod method,
        string path,
        object body
    ) =>
        client.SendAsync(
            new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) },
            Ct
        );

    public static async Task<HttpStatusCode> GetStatusAsync(this HttpClient client, string path) =>
        (await client.GetAsync(path, Ct)).StatusCode;

    /// <summary>The statuses of two writes sent at the same time.</summary>
    public static async Task<HttpStatusCode[]> RaceAsync(
        params Func<Task<HttpResponseMessage>>[] writes
    )
    {
        var responses = await Task.WhenAll(writes.Select(w => Task.Run(w, Ct)));
        return responses.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray();
    }
}

/// <summary>A partner of its own with a pickup point in Kazimierz and a customer about 2.3 km away (second fee band).</summary>
internal sealed record GapShop(
    HttpClient Admin,
    Guid PartnerId,
    string PartnerCode,
    Guid PointId,
    Guid CustomerId,
    Guid AddressId
)
{
    public static async Task<GapShop> CreateAsync(
        ApiFixture api,
        int commissionBps = 1500,
        string? username = null
    )
    {
        var admin = await api.AsAsync(username ?? ApiFixture.Admin);
        var partnerId = Guid.CreateVersion7();
        var code = $"G{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        await admin.PostAsync(
            ApiRoutes.Partners,
            new
            {
                id = partnerId,
                code,
                name = "Gap Test Grocer",
                kind = "grocery",
                commissionBps,
            }
        );
        var pointId = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.PickupPoints,
            new
            {
                id = pointId,
                partnerId,
                name = "Kazimierz",
                addressLine = "ul. Miodowa 22",
                city = "Kraków",
                postcode = "31-055",
                latitude = 50.0513m,
                longitude = 19.9446m,
                opensMinute = 0,
                closesMinute = 1440,
            }
        );
        var customerId = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.Customers,
            new
            {
                id = customerId,
                displayName = "Gap Tester",
                email = $"{customerId:N}@example.com",
            }
        );
        var addressId = Guid.CreateVersion7();
        await admin.PostAsync(
            ApiRoutes.CustomerAddresses.With(customerId),
            new
            {
                id = addressId,
                label = "Home",
                addressLine = "ul. Długa 15",
                city = "Kraków",
                postcode = "31-147",
                latitude = 50.0706m,
                longitude = 19.9377m,
            }
        );
        return new GapShop(admin, partnerId, code, pointId, customerId, addressId);
    }

    public async Task<Guid> ProductAsync(
        string sku,
        long priceMinor,
        int weightG = 600,
        string sizeClass = "S",
        bool ageRestricted = false
    )
    {
        var id = Guid.CreateVersion7();
        await Admin.PostAsync(
            ApiRoutes.Products,
            new
            {
                id,
                partnerId = PartnerId,
                sku,
                name = sku,
                category = "groceries",
                priceMinor,
                weightG,
                sizeClass,
                ageRestricted,
            }
        );
        return id;
    }

    /// <summary>The shop as DispatchTests' helpers take it (their products are not used by them).</summary>
    public DispatchTests.Shop Dispatch =>
        new(Admin, PointId, CustomerId, AddressId, Guid.Empty, Guid.Empty, Guid.Empty);

    public object OrderBody(
        Guid id,
        int tipPercent,
        params (Guid ProductId, int Quantity)[] lines
    ) =>
        new
        {
            id,
            customerId = CustomerId,
            addressId = AddressId,
            pickupPointId = PointId,
            lines = lines
                .Select(l => new { productId = l.ProductId, quantity = l.Quantity })
                .ToArray(),
            tipPercent,
        };

    public Task<JsonObject> PartnerAsync() =>
        Admin.GetObjectAsync(ApiRoutes.Partner.With(PartnerId));
}
