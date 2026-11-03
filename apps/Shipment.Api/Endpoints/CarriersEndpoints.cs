using Shipment.Application.Carriers;
using Shipment.Domain.Carriers;

namespace Shipment.Api.Endpoints;

internal sealed class CarriersEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags(ApiTags.Carriers);

        group
            .MapGet(
                ApiRoutes.VehicleTypes,
                (CarriersService carriers, CancellationToken ct) =>
                    carriers.ListVehicleTypesAsync(ct)
            )
            .WithName("Get vehicle types")
            .WithSummary("Bike, car and van with their limits");
        group
            .MapGet(
                ApiRoutes.Couriers,
                (
                    string? vehicleType,
                    Wire<ShiftStatus>? shiftStatus,
                    CarriersService carriers,
                    CancellationToken ct
                ) => carriers.ListCouriersAsync(vehicleType, shiftStatus?.Value, ct)
            )
            .WithName("Get couriers")
            .WithSummary("Couriers with their vehicle, shift and last known position");
        group
            .MapGet(
                ApiRoutes.Courier,
                async (Guid courierId, CarriersService carriers, CancellationToken ct) =>
                    ApiResults.Found(await carriers.GetCourierAsync(courierId, ct))
            )
            .WithName("Get courier")
            .WithSummary("A courier");
        group
            .MapPost(
                ApiRoutes.Couriers,
                async (CreateCourier request, CarriersService carriers, CancellationToken ct) =>
                    ApiResults.Created(
                        (await carriers.CreateCourierAsync(request, ct)).AsSaved(),
                        $"{ApiRoutes.Couriers}/{request.Id}"
                    )
            )
            .WithName("Create courier")
            .WithSummary("Add a courier, off shift; a car or a van needs its plate")
            .ProducesRefusals();
        group
            .MapPut(
                ApiRoutes.Courier,
                async (
                    Guid courierId,
                    CourierChange request,
                    CarriersService carriers,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await carriers.UpdateCourierAsync(
                                new UpdateCourier(
                                    courierId,
                                    request.DisplayName,
                                    request.Phone,
                                    request.Email,
                                    request.VehicleType,
                                    request.RowVersion,
                                    request.VehiclePlate
                                ),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Update courier")
            .WithSummary("Edit a courier; not the vehicle while delivering")
            .ProducesRefusals();
        group
            .MapPost(
                ApiRoutes.CourierShiftStart,
                async (
                    Guid courierId,
                    VersionOf request,
                    CarriersService carriers,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await carriers.StartShiftAsync(
                                new ChangeShift(courierId, request.RowVersion),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Start courier shift")
            .WithSummary("Put a courier on shift")
            .ProducesRefusals();
        group
            .MapPost(
                ApiRoutes.CourierShiftEnd,
                async (
                    Guid courierId,
                    VersionOf request,
                    CarriersService carriers,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await carriers.EndShiftAsync(
                                new ChangeShift(courierId, request.RowVersion),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("End courier shift")
            .WithSummary("Take a courier off shift; refused while delivering")
            .ProducesRefusals();
        group
            .MapPut(
                ApiRoutes.CourierPosition,
                async (
                    Guid courierId,
                    PositionChange request,
                    CarriersService carriers,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await carriers.SetPositionAsync(
                                new SetCourierPosition(
                                    courierId,
                                    request.Latitude,
                                    request.Longitude
                                ),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Change courier position")
            .WithSummary("Move a courier on the map (for the courier's phone); the latest wins")
            .ProducesRefusals();
    }

    internal sealed record CourierChange(
        string DisplayName,
        string Phone,
        string Email,
        string VehicleType,
        uint RowVersion,
        string? VehiclePlate = null
    );

    internal sealed record PositionChange(decimal Latitude, decimal Longitude);
}

/// <summary>The body of an action on a row: the version read.</summary>
internal sealed record VersionOf(uint RowVersion);
