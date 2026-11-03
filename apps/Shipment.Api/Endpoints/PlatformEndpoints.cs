using Shipment.Application.Platform;
using Shipment.Domain.Common;

namespace Shipment.Api.Endpoints;

internal sealed class PlatformEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags(ApiTags.Platform);

        // GET /api/v1/me - the signed-in admin
        group
            .MapGet(
                ApiRoutes.Me,
                async (PlatformService platform, ICurrentActor actor, CancellationToken ct) =>
                    ApiResults.Found(
                        actor.UserId is { } id ? await platform.GetUserAsync(id, ct) : null
                    )
            )
            .WithName("Get me")
            .WithSummary("The signed-in admin");

        // GET /api/v1/users
        group
            .MapGet(
                ApiRoutes.Users,
                (PlatformService platform, CancellationToken ct) => platform.ListUsersAsync(ct)
            )
            .WithName("Get users")
            .WithSummary("The operator's people");
    }
}
