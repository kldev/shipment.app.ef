using Shipment.Geo;

namespace Shipment.Domain.Common;

/// <summary>Straight-line distance in whole metres (haversine) - delivery fee bands, courier proposals, maps.</summary>
public static class Distance
{
    public static int Metres(decimal lat1, decimal lon1, decimal lat2, decimal lon2) =>
        (int)
            Math.Round(
                GeoLine.DistanceM(
                    new GeoPoint((double)lon1, (double)lat1),
                    new GeoPoint((double)lon2, (double)lat2)
                )
            );
}
