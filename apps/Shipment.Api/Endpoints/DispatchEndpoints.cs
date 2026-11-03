using Shipment.Application.Dispatch;
using Shipment.Domain.Dispatch;

namespace Shipment.Api.Endpoints;

internal sealed class DispatchEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags(ApiTags.Dispatch);

        group
            .MapGet(
                ApiRoutes.Shipments,
                (
                    Wire<ShipmentStatus>? status,
                    Guid? orderId,
                    Guid? courierId,
                    DispatchService dispatch,
                    CancellationToken ct
                ) =>
                    dispatch.ListShipmentsAsync(
                        new ShipmentFilter(status?.Value, orderId, courierId),
                        ct
                    )
            )
            .WithName("Get shipments")
            .WithSummary("Shipments, oldest waiting first");
        group
            .MapGet(
                ApiRoutes.Shipment,
                async (Guid shipmentId, DispatchService dispatch, CancellationToken ct) =>
                    ApiResults.Found(await dispatch.GetShipmentAsync(shipmentId, ct))
            )
            .WithName("Get shipment")
            .WithSummary("A shipment");
        group
            .MapGet(
                ApiRoutes.ShipmentProposals,
                (Guid shipmentId, DispatchService dispatch, CancellationToken ct) =>
                    dispatch.ProposalsAsync(shipmentId, ct)
            )
            .WithName("Get courier proposals")
            .WithSummary("Couriers who can take the shipment, nearest to the pickup point first");
        group
            .MapPost(
                ApiRoutes.ShipmentAssign,
                async (
                    Guid shipmentId,
                    AssignRequest request,
                    DispatchService dispatch,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await dispatch.AssignAsync(
                                new AssignCourier(
                                    shipmentId,
                                    request.CourierId,
                                    request.RowVersion
                                ),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Assign courier")
            .WithSummary("Give the shipment to a courier from the proposals")
            .ProducesRefusals();
        group
            .MapPost(
                ApiRoutes.ShipmentAdvance,
                async (
                    Guid shipmentId,
                    AdvanceRequest request,
                    DispatchService dispatch,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await dispatch.AdvanceAsync(
                                new AdvanceShipment(
                                    shipmentId,
                                    request.Status,
                                    request.RowVersion,
                                    request.AgeChecked
                                ),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Advance shipment")
            .WithSummary("The courier's next step; delivered needs the ID check for 18+ products")
            .ProducesRefusals();
        group
            .MapPost(
                ApiRoutes.ShipmentFail,
                async (
                    Guid shipmentId,
                    FailRequest request,
                    DispatchService dispatch,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await dispatch.FailAsync(
                                new FailShipment(shipmentId, request.Reason, request.RowVersion),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Fail shipment")
            .WithSummary("The delivery did not happen; it ends the order")
            .ProducesRefusals();
        group
            .MapPost(
                ApiRoutes.ShipmentUnassign,
                async (
                    Guid shipmentId,
                    VersionOf request,
                    DispatchService dispatch,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await dispatch.UnassignAsync(
                                new UnassignCourier(shipmentId, request.RowVersion),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Unassign courier")
            .WithSummary("Take the courier off before pickup")
            .ProducesRefusals();
    }

    internal sealed record AssignRequest(Guid CourierId, uint RowVersion);

    internal sealed record AdvanceRequest(
        ShipmentStatus Status,
        uint RowVersion,
        bool AgeChecked = false
    );

    internal sealed record FailRequest(string Reason, uint RowVersion);
}
