using Shipment.Geo;

namespace Shipment.Api.Web;

/// <summary>A GeoJSON LineString of a route leg as map points.</summary>
public static class GeoJson
{
    /// <summary>(latitude, longitude) of each [lon, lat] coordinate; empty when it is not a LineString.</summary>
    public static IReadOnlyList<(double Latitude, double Longitude)> Points(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return [];
        try
        {
            return GeoLine.Parse(line).Points.Select(p => (p.Latitude, p.Longitude)).ToList();
        }
        catch (Exception ex)
            when (ex
                    is System.Text.Json.JsonException
                        or KeyNotFoundException
                        or InvalidOperationException
                        or ArgumentException
            )
        {
            return [];
        }
    }
}
