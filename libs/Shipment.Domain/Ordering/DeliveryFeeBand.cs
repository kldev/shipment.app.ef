namespace Shipment.Domain.Ordering;

/// <summary>The delivery fee up to a straight-line distance (pickup point to customer, inclusive). Reference data.</summary>
public sealed class DeliveryFeeBand
{
    public int MaxDistanceM { get; init; }

    public long FeeMinor { get; init; }

    public static readonly IReadOnlyList<DeliveryFeeBand> All =
    [
        new() { MaxDistanceM = 2000, FeeMinor = 599 },
        new() { MaxDistanceM = 5000, FeeMinor = 899 },
        new() { MaxDistanceM = 10000, FeeMinor = 1299 },
        new() { MaxDistanceM = 40000, FeeMinor = 1999 },
    ];

    /// <summary>The fee of the first band that reaches the distance; null beyond the last (nothing is delivered).</summary>
    public static long? FeeFor(IEnumerable<DeliveryFeeBand> bands, int distanceM) =>
        bands
            .Where(b => b.MaxDistanceM >= distanceM)
            .OrderBy(b => b.MaxDistanceM)
            .FirstOrDefault()
            ?.FeeMinor;
}
