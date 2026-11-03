namespace Shipment.Domain.Billing;

/// <summary>What a courier earns for one delivery by vehicle: a base plus per km of the route (both legs).</summary>
public sealed class PayoutRate
{
    public required string VehicleType { get; init; }

    public long BaseMinor { get; init; }

    public long PerKmMinor { get; init; }

    public long PayoutFor(long metres) =>
        BaseMinor + (long)Math.Round(PerKmMinor * metres / 1000m, MidpointRounding.AwayFromZero);

    public static readonly IReadOnlyList<PayoutRate> All =
    [
        new()
        {
            VehicleType = "bike",
            BaseMinor = 600,
            PerKmMinor = 120,
        },
        new()
        {
            VehicleType = "car",
            BaseMinor = 800,
            PerKmMinor = 160,
        },
        new()
        {
            VehicleType = "van",
            BaseMinor = 1200,
            PerKmMinor = 220,
        },
    ];
}

/// <summary>An integration event billing has booked (its outbox id): the same event never books twice.</summary>
public sealed class ProcessedEvent
{
    public Guid EventId { get; init; }

    public required string MessageType { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}
