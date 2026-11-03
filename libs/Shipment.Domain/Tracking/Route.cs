namespace Shipment.Domain.Tracking;

public enum RouteLeg
{
    ToPickup,
    ToCustomer,
}

/// <summary>A leg as the router gave it: a GeoJSON LineString, metres and seconds.</summary>
public sealed record LegData(string GeoJson, int DistanceM, int DurationS);

/// <summary>
/// Where the courier goes (tracking.routes): two legs - courier to the pickup point, pickup point to the customer - as
/// GeoJSON with the router's distance and duration; approximate = straight lines when the router did not answer.
/// promised_at is set with the route: assigned + both legs + 10 minutes. The simulator's place lives here too.
/// </summary>
public sealed class Route
{
    public static readonly TimeSpan PromiseMargin = TimeSpan.FromMinutes(10);

    private Route() { }

    public Route(Guid shipmentId) => ShipmentId = shipmentId;

    public Guid ShipmentId { get; private set; }

    public Guid CourierId { get; private set; }

    /// <summary>The router's profile: bike, or car for cars and vans.</summary>
    public string Profile { get; private set; } = "";

    public bool Approximate { get; private set; }

    public string ToPickup { get; private set; } = "";

    public int ToPickupDistanceM { get; private set; }

    public int ToPickupDurationS { get; private set; }

    public string ToCustomer { get; private set; } = "";

    public int ToCustomerDistanceM { get; private set; }

    public int ToCustomerDurationS { get; private set; }

    public DateTimeOffset ComputedAt { get; private set; }

    public DateTimeOffset PromisedAt { get; private set; }

    public bool Simulated { get; private set; }

    public RouteLeg? SimLeg { get; private set; }

    public int SimTravelledM { get; private set; }

    public DateTimeOffset ModifiedAt { get; private set; }

    /// <summary>A new route for a courier - the simulator starts over.</summary>
    public void Set(
        Guid courierId,
        string profile,
        bool approximate,
        LegData toPickup,
        LegData toCustomer,
        DateTimeOffset assignedAt,
        DateTimeOffset now
    )
    {
        CourierId = courierId;
        Profile = profile;
        Approximate = approximate;
        (ToPickup, ToPickupDistanceM, ToPickupDurationS) = (
            toPickup.GeoJson,
            toPickup.DistanceM,
            toPickup.DurationS
        );
        (ToCustomer, ToCustomerDistanceM, ToCustomerDurationS) = (
            toCustomer.GeoJson,
            toCustomer.DistanceM,
            toCustomer.DurationS
        );
        ComputedAt = now;
        PromisedAt =
            assignedAt
            + TimeSpan.FromSeconds(toPickup.DurationS + toCustomer.DurationS)
            + PromiseMargin;
        Simulated = false;
        SimLeg = null;
        SimTravelledM = 0;
        ModifiedAt = now;
    }

    public bool SetSimulated(bool simulated, DateTimeOffset now)
    {
        if (Simulated == simulated)
            return false;
        Simulated = simulated;
        ModifiedAt = now;
        return true;
    }

    public void Progress(RouteLeg leg, int travelledM, DateTimeOffset now)
    {
        SimLeg = leg;
        SimTravelledM = Math.Max(travelledM, 0);
        ModifiedAt = now;
    }
}
