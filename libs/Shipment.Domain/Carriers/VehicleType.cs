using Shipment.Domain.Common;

namespace Shipment.Domain.Carriers;

/// <summary>
/// Bike, car or van with what one trip may carry (weight, largest size class) and how far; reference data the
/// operator keeps. Dispatch picks the smallest one that fits (by <see cref="SortOrder"/>).
/// </summary>
public sealed class VehicleType
{
    public const string Bike = "bike";
    public const string Car = "car";
    public const string Van = "van";

    public required string Code { get; init; }

    public required string Name { get; init; }

    public int MaxLoadG { get; init; }

    public SizeClass MaxSizeClass { get; init; }

    /// <summary>The longest straight-line delivery it takes, pickup point to customer.</summary>
    public int MaxDistanceM { get; init; }

    /// <summary>Average speed in the city - the straight-line route's duration.</summary>
    public int AvgSpeedKmh { get; init; }

    /// <summary>A car or a van: the courier needs a driving licence and vehicle insurance.</summary>
    public bool NeedsLicence { get; init; }

    public int SortOrder { get; init; }

    public bool Carries(int weightG, SizeClass largest, int distanceM) =>
        MaxLoadG >= weightG && MaxSizeClass >= largest && MaxDistanceM >= distanceM;

    public static readonly IReadOnlyList<VehicleType> All =
    [
        new()
        {
            Code = Bike,
            Name = "Bike",
            MaxLoadG = 8000,
            MaxSizeClass = SizeClass.M,
            MaxDistanceM = 5000,
            AvgSpeedKmh = 15,
            NeedsLicence = false,
            SortOrder = 1,
        },
        new()
        {
            Code = Car,
            Name = "Car",
            MaxLoadG = 40000,
            MaxSizeClass = SizeClass.L,
            MaxDistanceM = 25000,
            AvgSpeedKmh = 25,
            NeedsLicence = true,
            SortOrder = 2,
        },
        new()
        {
            Code = Van,
            Name = "Van",
            MaxLoadG = 500000,
            MaxSizeClass = SizeClass.XL,
            MaxDistanceM = 40000,
            AvgSpeedKmh = 22,
            NeedsLicence = true,
            SortOrder = 3,
        },
    ];
}
