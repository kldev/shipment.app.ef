using Shipment.Application.Ordering;
using Shipment.Domain.Ordering;

namespace Shipment.Api.Endpoints;

internal sealed class OrderingEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags(ApiTags.Ordering);

        group
            .MapGet(
                ApiRoutes.Orders,
                (
                    Wire<OrderStatus>? status,
                    Guid? customerId,
                    Guid? partnerId,
                    Guid? pickupPointId,
                    int? take,
                    OrderingService ordering,
                    CancellationToken ct
                ) =>
                    ordering.ListOrdersAsync(
                        new OrderFilter(status?.Value, customerId, partnerId, pickupPointId),
                        take ?? 200,
                        ct
                    )
            )
            .WithName("Get orders")
            .WithSummary("Orders, newest first, with their totals");
        group
            .MapGet(
                ApiRoutes.Order,
                async (Guid orderId, OrderingService ordering, CancellationToken ct) =>
                    ApiResults.Found(await ordering.GetOrderAsync(orderId, ct))
            )
            .WithName("Get order")
            .WithSummary("An order");
        group
            .MapGet(
                ApiRoutes.OrderLines,
                (Guid orderId, OrderingService ordering, CancellationToken ct) =>
                    ordering.ListLinesAsync(orderId, ct)
            )
            .WithName("Get order lines")
            .WithSummary("The lines of an order: ordered, packed and missing");
        group
            .MapGet(
                ApiRoutes.Quote,
                async (
                    Guid pickupPointId,
                    Guid addressId,
                    OrderingService ordering,
                    CancellationToken ct
                ) => ApiResults.Found(await ordering.QuoteAsync(pickupPointId, addressId, ct))
            )
            .WithName("Get quote")
            .WithSummary("Distance and delivery fee from a pickup point to an address");
        group
            .MapGet(
                ApiRoutes.DeliveryFeeBands,
                (OrderingService ordering, CancellationToken ct) => ordering.ListFeeBandsAsync(ct)
            )
            .WithName("Get delivery fee bands")
            .WithSummary("The delivery fee by straight-line distance");
        // POST /api/v1/orders - the id comes from the client (ADR 0009)
        group
            .MapPost(
                ApiRoutes.Orders,
                async (PlaceOrder request, OrderingService ordering, CancellationToken ct) =>
                    ApiResults.Created(
                        await ordering.PlaceAsync(request, ct),
                        $"{ApiRoutes.Orders}/{request.Id}"
                    )
            )
            .WithName("Place order")
            .WithSummary("Place an order for a customer from one partner's open pickup point")
            .ProducesRefusals();
        group
            .MapPost(
                ApiRoutes.OrderCancel,
                async (
                    Guid orderId,
                    CancelRequest request,
                    OrderingService ordering,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await ordering.CancelAsync(
                                new CancelOrder(orderId, request.Reason, request.RowVersion),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Cancel order")
            .WithSummary("Cancel an order the store has not started packing")
            .ProducesRefusals();

        group
            .MapGet(
                ApiRoutes.PickingQueue,
                (Guid? pickupPointId, PickingService picking, CancellationToken ct) =>
                    picking.QueueAsync(pickupPointId, ct)
            )
            .WithName("Get picking queue")
            .WithSummary("Orders the stores have to pack, oldest first");
        group
            .MapGet(
                ApiRoutes.PickingHistory,
                (
                    Guid? pickupPointId,
                    DateTimeOffset? from,
                    DateTimeOffset? to,
                    bool? hasMissing,
                    int? take,
                    PickingService picking,
                    CancellationToken ct
                ) =>
                    picking.HistoryAsync(
                        new PickingHistoryFilter(pickupPointId, from, to, hasMissing),
                        take ?? 50,
                        ct
                    )
            )
            .WithName("Get picking history")
            .WithSummary("Finished pickings, newest first");
        group
            .MapPost(
                ApiRoutes.OrderPickingStart,
                async (
                    Guid orderId,
                    VersionOf request,
                    PickingService picking,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await picking.StartAsync(
                                new StartPicking(orderId, request.RowVersion),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Start picking")
            .WithSummary("The store starts packing an order")
            .ProducesRefusals();
        group
            .MapPost(
                ApiRoutes.OrderPickingFinish,
                async (
                    Guid orderId,
                    FinishRequest request,
                    PickingService picking,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        await picking.FinishAsync(
                            new FinishPicking(orderId, request.Lines, request.RowVersion),
                            ct
                        )
                    )
            )
            .WithName("Finish picking")
            .WithSummary("Every line packed or missing; nothing packed cancels the order")
            .ProducesRefusals();
    }

    internal sealed record CancelRequest(string Reason, uint RowVersion);

    internal sealed record FinishRequest(IReadOnlyList<LinePick> Lines, uint RowVersion);
}
