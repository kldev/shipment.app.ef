using System.Net;
using Shipment.Api.Endpoints;

namespace Shipment.Tests;

/// <summary>Regressions for the findings of the review of the EF Core rewrite (2026-11-01).</summary>
[Trait("verify", "crm")]
[Collection(ApiCollection.Name)]
public sealed class ReviewRegressionTests(ApiFixture api)
{
    private static readonly HttpMethod Post = HttpMethod.Post;

    private static string Code() => $"R{Guid.NewGuid():N}"[..10].ToUpperInvariant();

    private static object Partner(Guid id, string code, string name = "Review Grocer") =>
        new
        {
            id,
            code,
            name,
            kind = "grocery",
            commissionBps = 1500,
        };

    [Fact]
    public async Task A_text_longer_than_its_column_is_a_400_not_a_500()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        await admin.RefusedAsync(
            Post,
            ApiRoutes.Partners,
            Partner(Guid.CreateVersion7(), Code(), new string('x', 201)),
            HttpStatusCode.BadRequest,
            "longer than its field allows"
        );
    }

    [Fact]
    public async Task The_same_create_sent_twice_at_once_answers_the_same_both_times()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var (id, code) = (Guid.CreateVersion7(), Code());
        var statuses = await GapCalls.RaceAsync(
            () => admin.SendRawAsync(Post, ApiRoutes.Partners, Partner(id, code)),
            () => admin.SendRawAsync(Post, ApiRoutes.Partners, Partner(id, code))
        );
        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Created, s));
        Assert.Equal(
            1L,
            await api.ScalarAsync<long>($"SELECT count(*) FROM crm.partners WHERE id = '{id}'")
        );
    }

    [Fact]
    public async Task Null_lines_are_a_400()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        var place = await admin.SendRawAsync(
            Post,
            ApiRoutes.Orders,
            new
            {
                id = Guid.CreateVersion7(),
                customerId = Guid.CreateVersion7(),
                addressId = Guid.CreateVersion7(),
                pickupPointId = Guid.CreateVersion7(),
                lines = new object?[] { null },
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, place.StatusCode);
        var finish = await admin.SendRawAsync(
            Post,
            ApiRoutes.OrderPickingFinish.With(Guid.CreateVersion7()),
            new { lines = (object?)null, rowVersion = 1 }
        );
        Assert.Equal(HttpStatusCode.BadRequest, finish.StatusCode);
    }

    [Fact]
    public async Task A_position_wins_even_when_the_courier_changed_meanwhile()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        // A demo courier on shift; the position has no row version, the latest one simply wins.
        var courierId = Guid.Parse("019a2f15-0000-7000-8000-000000000002");
        await admin.PutAsync(
            ApiRoutes.CourierPosition.With(courierId),
            new { latitude = 50.06m, longitude = 19.94m }
        );
        var moved = await admin.PutAsync(
            ApiRoutes.CourierPosition.With(courierId),
            new { latitude = 50.07m, longitude = 19.95m }
        );
        Assert.True(moved.RowVersion() > 0);
        Assert.Equal(
            50.07m,
            await api.ScalarAsync<decimal>(
                $"SELECT last_latitude FROM carriers.couriers WHERE id = '{courierId}'"
            )
        );
        Assert.Equal(
            ApiFixture.AdminId,
            await api.ScalarAsync<Guid>(
                $"SELECT modified_by FROM carriers.couriers WHERE id = '{courierId}'"
            )
        );
    }
}
