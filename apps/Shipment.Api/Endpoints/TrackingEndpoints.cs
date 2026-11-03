using Shipment.Application.Tracking;
using Shipment.Domain.Dispatch;

namespace Shipment.Api.Endpoints;

internal sealed class TrackingEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags(ApiTags.Tracking);

        group
            .MapGet(
                ApiRoutes.Tracking,
                (Wire<ShipmentStatus>? status, TrackingService tracking, CancellationToken ct) =>
                    tracking.ListTrackingAsync(null, status?.Value, ct)
            )
            .WithName("Get tracking")
            .WithSummary("Where the shipments are and when they arrive");
        group
            .MapGet(
                ApiRoutes.ShipmentTracking,
                async (Guid shipmentId, TrackingService tracking, CancellationToken ct) =>
                    ApiResults.Found(
                        (await tracking.ListTrackingAsync(shipmentId, null, ct)).FirstOrDefault()
                    )
            )
            .WithName("Get shipment tracking")
            .WithSummary("A shipment's route, position, next stop and ETA");
        group
            .MapGet(
                ApiRoutes.ShipmentPings,
                (Guid shipmentId, TrackingService tracking, CancellationToken ct) =>
                    tracking.ListPingsAsync(shipmentId, ct)
            )
            .WithName("Get shipment pings")
            .WithSummary("The path driven so far");
        group
            .MapPut(
                ApiRoutes.ShipmentSimulation,
                async (
                    Guid shipmentId,
                    SimulationRequest request,
                    TrackingService tracking,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        await tracking.SetSimulationAsync(
                            new SetSimulation(shipmentId, request.Simulated),
                            ct
                        )
                    )
            )
            .WithName("Change shipment simulation")
            .WithSummary("Let the demo's simulator move the courier along the route, or stop it")
            .ProducesRefusals();
    }

    internal sealed record SimulationRequest(bool Simulated);
}
