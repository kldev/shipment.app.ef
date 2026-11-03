using Microsoft.EntityFrameworkCore;
using Shipment.Domain.Carriers;
using Shipment.Domain.Common;
using Shipment.Domain.Dispatch;
using Shipment.Domain.Tracking;
using Shipment.Persistence;

namespace Shipment.Application.Tracking;

public enum NextStop
{
    Pickup,
    Customer,
}

/// <summary>
/// Where a shipment is and when it arrives: the route, the courier's position (the last ping, else carriers' last
/// position), the next stop, what is left and the ETA - the router's time of each leg for the part still ahead.
/// </summary>
public sealed record TrackingRow(
    Guid ShipmentId,
    string Code,
    ShipmentStatus Status,
    string OrderCode,
    string CustomerName,
    string PartnerName,
    string PickupPointName,
    string DropoffAddressLine,
    Guid CourierId,
    string? CourierName,
    string? VehicleType,
    string Profile,
    bool Approximate,
    string ToPickup,
    int ToPickupDistanceM,
    int ToPickupDurationS,
    string ToCustomer,
    int ToCustomerDistanceM,
    int ToCustomerDurationS,
    bool Simulated,
    decimal PickupLatitude,
    decimal PickupLongitude,
    decimal DropoffLatitude,
    decimal DropoffLongitude,
    decimal? Latitude,
    decimal? Longitude,
    DateTimeOffset? PositionAt,
    NextStop? NextStop,
    int RemainingM,
    int RemainingS,
    DateTimeOffset? EtaAt,
    DateTimeOffset PromisedAt,
    DateTimeOffset ComputedAt,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? DeliveredAt,
    bool Late
);

public sealed record PingRow(
    long Id,
    Guid ShipmentId,
    Guid CourierId,
    RouteLeg Leg,
    decimal Latitude,
    decimal Longitude,
    DateTimeOffset RecordedAt
);

/// <summary>What the runner asks the router for: from the courier (the pickup point when unknown) to the pickup point, then on.</summary>
public sealed record RouteRequest(
    Guid CourierId,
    string Profile,
    int AvgSpeedKmh,
    decimal CourierLatitude,
    decimal CourierLongitude,
    decimal PickupLatitude,
    decimal PickupLongitude,
    decimal DropoffLatitude,
    decimal DropoffLongitude
);

public sealed record SetRoute(
    Guid ShipmentId,
    Guid CourierId,
    string Profile,
    bool Approximate,
    LegData ToPickup,
    LegData ToCustomer
);

/// <summary>A simulated shipment on the way: its current leg's line and how far along it the courier is.</summary>
public sealed record SimulationStep(
    Guid ShipmentId,
    Guid CourierId,
    RouteLeg Leg,
    int TravelledM,
    string Line,
    int LegDistanceM
);

public sealed record SetSimulation(Guid ShipmentId, bool Simulated);

/// <summary>The tracking context: routes in two legs, the positions along them and the demo's simulator.</summary>
public sealed class TrackingService(IDbContextFactory<ShipmentDbContext> factory, TimeProvider time)
{
    public async Task<IReadOnlyList<TrackingRow>> ListTrackingAsync(
        Guid? shipmentId = null,
        ShipmentStatus? status = null,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var routes = db.Routes.AsQueryable();
        if (shipmentId is { } id)
            routes = routes.Where(r => r.ShipmentId == id);
        var rows = await routes
            .Join(db.Shipments, r => r.ShipmentId, s => s.Id, (r, s) => new { r, s })
            .Where(x => status == null || x.s.Status == status)
            .OrderBy(x => x.s.CreatedAt)
            .Take(200)
            .Select(x => new
            {
                x.r,
                x.s,
                // ReSharper disable once AccessToDisposedClosure - an expression tree, translated to SQL before the context is disposed
                Courier = db
                    .Couriers.Where(c => c.Id == x.r.CourierId)
                    .Select(c => new
                    {
                        c.DisplayName,
                        c.VehicleType,
                        c.LastLatitude,
                        c.LastLongitude,
                        c.LastPositionAt,
                    })
                    .FirstOrDefault(),
                // ReSharper disable once AccessToDisposedClosure - an expression tree, translated to SQL before the context is disposed
                Ping = db
                    .Pings.Where(p => p.ShipmentId == x.r.ShipmentId)
                    .OrderByDescending(p => p.Id)
                    .Select(p => new
                    {
                        p.Latitude,
                        p.Longitude,
                        p.RecordedAt,
                    })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);
        var now = time.GetUtcNow();
        return rows.Select(x =>
            {
                var (r, s) = (x.r, x.s);
                var leftToPickup =
                    s.Status == ShipmentStatus.CourierAssigned
                        ? Math.Max(
                            r.ToPickupDistanceM
                                - (r.SimLeg == RouteLeg.ToPickup ? r.SimTravelledM : 0),
                            0
                        )
                        : 0;
                var leftToCustomer = s.Status switch
                {
                    ShipmentStatus.CourierAssigned or ShipmentStatus.ArrivedAtPickup =>
                        r.ToCustomerDistanceM,
                    ShipmentStatus.PickedUp => Math.Max(
                        r.ToCustomerDistanceM
                            - (r.SimLeg == RouteLeg.ToCustomer ? r.SimTravelledM : 0),
                        0
                    ),
                    _ => 0,
                };
                var remainingS = s.IsActive
                    ? Seconds(leftToPickup, r.ToPickupDistanceM, r.ToPickupDurationS)
                        + Seconds(leftToCustomer, r.ToCustomerDistanceM, r.ToCustomerDurationS)
                    : 0;
                DateTimeOffset? eta = s.Status switch
                {
                    ShipmentStatus.Delivered => s.DeliveredAt,
                    ShipmentStatus.Failed => null,
                    _ => now.AddSeconds(remainingS),
                };
                return new TrackingRow(
                    r.ShipmentId,
                    s.Code,
                    s.Status,
                    s.OrderCode,
                    s.CustomerName,
                    s.PartnerName,
                    s.PickupPointName,
                    s.DropoffAddressLine,
                    r.CourierId,
                    x.Courier?.DisplayName,
                    x.Courier?.VehicleType,
                    r.Profile,
                    r.Approximate,
                    r.ToPickup,
                    r.ToPickupDistanceM,
                    r.ToPickupDurationS,
                    r.ToCustomer,
                    r.ToCustomerDistanceM,
                    r.ToCustomerDurationS,
                    r.Simulated,
                    s.PickupLatitude,
                    s.PickupLongitude,
                    s.DropoffLatitude,
                    s.DropoffLongitude,
                    x.Ping?.Latitude ?? x.Courier?.LastLatitude,
                    x.Ping?.Longitude ?? x.Courier?.LastLongitude,
                    x.Ping?.RecordedAt ?? x.Courier?.LastPositionAt,
                    s.Status switch
                    {
                        ShipmentStatus.CourierAssigned or ShipmentStatus.ArrivedAtPickup =>
                            NextStop.Pickup,
                        ShipmentStatus.PickedUp or ShipmentStatus.ArrivedAtCustomer =>
                            NextStop.Customer,
                        _ => null,
                    },
                    leftToPickup + leftToCustomer,
                    remainingS,
                    eta,
                    r.PromisedAt,
                    r.ComputedAt,
                    s.AssignedAt,
                    s.PickedUpAt,
                    s.DeliveredAt,
                    eta > r.PromisedAt
                );
            })
            .ToList();
    }

    public async Task<IReadOnlyList<PingRow>> ListPingsAsync(
        Guid shipmentId,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db
            .Pings.Where(p => p.ShipmentId == shipmentId)
            .OrderBy(p => p.Id)
            .Select(p => new PingRow(
                p.Id,
                p.ShipmentId,
                p.CourierId,
                p.Leg,
                p.Latitude,
                p.Longitude,
                p.RecordedAt
            ))
            .ToListAsync(ct);
    }

    /// <summary>The demo's simulator for one shipment, on or off; the same state again is a no-op. Answers the state.</summary>
    public async Task<Result<bool>> SetSimulationAsync(
        SetSimulation command,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var route = await db.Routes.FindAsync([command.ShipmentId], ct);
        if (route is null)
            return Error.NotFound(
                "No route yet - the runner fetches it right after the courier is assigned."
            );
        if (route.SetSimulated(command.Simulated, time.GetUtcNow()))
            await db.SaveChangesAsync(ct);
        return route.Simulated;
    }

    // ---- The runner (ADR 0007) ----

    /// <summary>The route to ask for on courier-assigned; null while the shipment has no courier.</summary>
    public async Task<RouteRequest?> RouteRequestAsync(
        Guid shipmentId,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db
            .Shipments.Where(s => s.Id == shipmentId && s.CourierId != null)
            .Join(db.Couriers, s => s.CourierId, c => c.Id, (s, c) => new { s, c })
            .Join(
                db.VehicleTypes,
                x => x.c.VehicleType,
                v => v.Code,
                (x, v) =>
                    new
                    {
                        x.s,
                        x.c,
                        v,
                    }
            )
            .Select(x => new RouteRequest(
                x.c.Id,
                x.c.VehicleType == VehicleType.Bike ? "bike" : "car",
                x.v.AvgSpeedKmh,
                x.c.LastLatitude ?? x.s.PickupLatitude,
                x.c.LastLongitude ?? x.s.PickupLongitude,
                x.s.PickupLatitude,
                x.s.PickupLongitude,
                x.s.DropoffLatitude,
                x.s.DropoffLongitude
            ))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Stores the fetched route. The same courier again is a no-op (a repeated event); another courier (reassigned)
    /// replaces it and starts the simulator over. Promised = assigned + both legs + 10 minutes.
    /// </summary>
    public async Task<Result> SetRouteAsync(SetRoute command, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var route = await db.Routes.FindAsync([command.ShipmentId], ct);
        if (route is not null && route.CourierId == command.CourierId)
            return Result.Success;
        var assignedAt = await db
            .Shipments.Where(s => s.Id == command.ShipmentId)
            .Select(s => new { s.AssignedAt })
            .FirstOrDefaultAsync(ct);
        if (assignedAt is null)
            return Error.NotFound("Shipment not found.");
        var now = time.GetUtcNow();
        if (route is null)
        {
            route = new Route(command.ShipmentId);
            db.Routes.Add(route);
        }
        route.Set(
            command.CourierId,
            command.Profile,
            command.Approximate,
            command.ToPickup,
            command.ToCustomer,
            assignedAt.AssignedAt ?? now,
            now
        );
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    /// <summary>
    /// The simulator's pass: simulated shipments on the way - to the pickup point while courier assigned, to the customer
    /// while picked up. At the end of a leg it waits for the status button, so a leg already driven is not listed.
    /// </summary>
    public async Task<IReadOnlyList<SimulationStep>> SimulationDueAsync(
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db
            .Routes.Where(r => r.Simulated)
            .Join(
                db.Shipments,
                r => new { Id = r.ShipmentId, Courier = (Guid?)r.CourierId },
                s => new { s.Id, Courier = s.CourierId },
                (r, s) => new { r, s.Status }
            )
            .Where(x =>
                x.Status == ShipmentStatus.CourierAssigned || x.Status == ShipmentStatus.PickedUp
            )
            .ToListAsync(ct);
        return rows.Select(x =>
            {
                var leg =
                    x.Status == ShipmentStatus.CourierAssigned
                        ? RouteLeg.ToPickup
                        : RouteLeg.ToCustomer;
                var (line, distance) =
                    leg == RouteLeg.ToPickup
                        ? (x.r.ToPickup, x.r.ToPickupDistanceM)
                        : (x.r.ToCustomer, x.r.ToCustomerDistanceM);
                var onThisLeg = x.r.SimLeg == leg;
                return (
                    Driven: onThisLeg && x.r.SimTravelledM >= distance,
                    Step: new SimulationStep(
                        x.r.ShipmentId,
                        x.r.CourierId,
                        leg,
                        onThisLeg ? x.r.SimTravelledM : 0,
                        line,
                        distance
                    )
                );
            })
            .Where(x => !x.Driven)
            .Select(x => x.Step)
            .ToList();
    }

    /// <summary>A position on a leg and the simulator's progress; the courier's last position is carriers' own write.</summary>
    public async Task<Result> RecordPingAsync(
        Guid shipmentId,
        RouteLeg leg,
        int travelledM,
        decimal latitude,
        decimal longitude,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var route = await db.Routes.FindAsync([shipmentId], ct);
        if (route is null)
            return Error.NotFound("Route not found.");
        var now = time.GetUtcNow();
        route.Progress(leg, travelledM, now);
        db.Pings.Add(
            new Ping(
                shipmentId,
                route.CourierId,
                leg,
                Text.Coordinate(latitude),
                Text.Coordinate(longitude),
                now
            )
        );
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    /// <summary>The router's time for the part of a leg still ahead (left / leg length).</summary>
    private static int Seconds(int leftM, int legM, int legS) =>
        legM == 0 ? 0 : (int)Math.Round((decimal)leftM / legM * legS);
}
