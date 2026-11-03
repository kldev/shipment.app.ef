using JetBrains.Annotations;
using Shipment.Application.Tracking;
using Shipment.Domain.Tracking;
using Shipment.Geo;
using Shipment.Messaging.Contracts;
using Shipment.Runner.Messaging;

namespace Shipment.Runner.Tracking;

/// <summary>
/// Tracking's reaction to courier-assigned (ADR 0007): the route in two legs - the courier to the pickup point, the
/// pickup point to the customer - from OSRM by the vehicle's profile (the same courier again changes nothing). When
/// OSRM does not answer after its retries, the legs are straight lines at the vehicle's average speed, marked
/// approximate, so the page still draws something.
/// </summary>
// Found by Wolverine (a public class with Handle in the runner's assembly).
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class RouteFetchHandler(
    TrackingService tracking,
    OsrmClient osrm,
    ILogger<RouteFetchHandler> logger
)
{
    public async Task Handle(CourierAssigned message, CancellationToken ct)
    {
        var request = await tracking.RouteRequestAsync(message.ShipmentId, ct);
        if (request is null)
        {
            logger.LogInformation(
                "Route of {Code} skipped: the shipment has no courier now",
                message.Code
            );
            return;
        }
        // A courier assigned again later must be the one the route is for; an older event is not.
        if (request.CourierId != message.CourierId)
        {
            logger.LogInformation(
                "Route of {Code} skipped: another courier has it now",
                message.Code
            );
            return;
        }
        var courier = new GeoPoint(
            (double)request.CourierLongitude,
            (double)request.CourierLatitude
        );
        var pickup = new GeoPoint((double)request.PickupLongitude, (double)request.PickupLatitude);
        var dropoff = new GeoPoint(
            (double)request.DropoffLongitude,
            (double)request.DropoffLatitude
        );

        RouteLeg toPickup,
            toCustomer;
        var approximate = false;
        try
        {
            toPickup = await osrm.RouteAsync(request.Profile, courier, pickup, ct);
            toCustomer = await osrm.RouteAsync(request.Profile, pickup, dropoff, ct);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "OSRM did not answer for {Code}: straight lines instead",
                message.Code
            );
            approximate = true;
            toPickup = Straight(courier, pickup, request.AvgSpeedKmh);
            toCustomer = Straight(pickup, dropoff, request.AvgSpeedKmh);
        }

        (
            await tracking.SetRouteAsync(
                new SetRoute(
                    message.ShipmentId,
                    request.CourierId,
                    request.Profile,
                    approximate,
                    new LegData(toPickup.Line.ToGeoJson(), toPickup.DistanceM, toPickup.DurationS),
                    new LegData(
                        toCustomer.Line.ToGeoJson(),
                        toCustomer.DistanceM,
                        toCustomer.DurationS
                    )
                ),
                ct
            )
        ).ThrowIfFailed("Set route");
        logger.LogInformation(
            "Route of {Code}: {ToPickup} m to the pickup point, {ToCustomer} m to the customer{Approximate}",
            message.Code,
            toPickup.DistanceM,
            toCustomer.DistanceM,
            approximate ? " (approximate)" : ""
        );
    }

    /// <summary>A straight leg at the vehicle's average speed.</summary>
    public static RouteLeg Straight(GeoPoint from, GeoPoint to, int speedKmh)
    {
        var line = GeoLine.Straight(from, to);
        var metres = (int)Math.Round(line.LengthM);
        return new RouteLeg(line, metres, (int)Math.Round(metres / (speedKmh / 3.6)));
    }
}
