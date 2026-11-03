namespace Shipment.Domain.Tracking;

/// <summary>A position of the courier on a shipment's leg (the simulator writes them; a phone would).</summary>
public sealed class Ping
{
    private Ping() { }

    public Ping(
        Guid shipmentId,
        Guid courierId,
        RouteLeg leg,
        decimal latitude,
        decimal longitude,
        DateTimeOffset at
    )
    {
        ShipmentId = shipmentId;
        CourierId = courierId;
        Leg = leg;
        Latitude = latitude;
        Longitude = longitude;
        RecordedAt = at;
    }

    public long Id { get; private set; }

    public Guid ShipmentId { get; private set; }

    public Guid CourierId { get; private set; }

    public RouteLeg Leg { get; private set; }

    public decimal Latitude { get; private set; }

    public decimal Longitude { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }
}
