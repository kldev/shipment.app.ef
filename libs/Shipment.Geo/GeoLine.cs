using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shipment.Geo;

/// <summary>A point as GeoJSON orders it: longitude first.</summary>
public readonly record struct GeoPoint(double Longitude, double Latitude);

/// <summary>
/// A GeoJSON LineString of a route leg (as OSRM returns it with geometries=geojson): its length on the sphere and the
/// point a courier reaches after so many metres along it - the step of the simulator.
/// </summary>
public sealed class GeoLine
{
    private const double EarthRadiusM = 6_371_000;

    private readonly double[] _cumulative;

    private GeoLine(IReadOnlyList<GeoPoint> points)
    {
        if (points.Count == 0)
            throw new ArgumentException("A line needs at least one point.", nameof(points));
        Points = points;
        _cumulative = new double[points.Count];
        for (var i = 1; i < points.Count; i++)
            _cumulative[i] = _cumulative[i - 1] + DistanceM(points[i - 1], points[i]);
    }

    public IReadOnlyList<GeoPoint> Points { get; }

    public double LengthM => _cumulative[^1];

    /// <summary>The point <paramref name="metres"/> along the line; before the start the start, past the end the end.</summary>
    public GeoPoint PointAt(double metres)
    {
        if (metres <= 0 || Points.Count == 1)
            return Points[0];
        if (metres >= LengthM)
            return Points[^1];
        var i = Array.BinarySearch(_cumulative, metres);
        if (i >= 0)
            return Points[i];
        var after = ~i;
        var before = after - 1;
        var segment = _cumulative[after] - _cumulative[before];
        var t = segment == 0 ? 0 : (metres - _cumulative[before]) / segment;
        return new GeoPoint(
            Points[before].Longitude + (Points[after].Longitude - Points[before].Longitude) * t,
            Points[before].Latitude + (Points[after].Latitude - Points[before].Latitude) * t
        );
    }

    /// <summary>
    /// A courier's step along a route leg: the metres along it after the step (never past the leg's OSRM distance) and
    /// the point there. OSRM's distance and the line's own length differ a little, so the point is taken at the same
    /// share of the line.
    /// </summary>
    public (int TravelledM, GeoPoint Point) Step(int legDistanceM, int travelledM, int stepM)
    {
        var next = Math.Min(legDistanceM, travelledM + stepM);
        var share = legDistanceM == 0 ? 1 : (double)next / legDistanceM;
        return (next, PointAt(share * LengthM));
    }

    /// <summary>Haversine - the same as platform.distance_m in the database.</summary>
    public static double DistanceM(GeoPoint a, GeoPoint b)
    {
        var lat1 = double.DegreesToRadians(a.Latitude);
        var lat2 = double.DegreesToRadians(b.Latitude);
        var dLat = lat2 - lat1;
        var dLon = double.DegreesToRadians(b.Longitude - a.Longitude);
        var h =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusM * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>A straight line between two points - the route when OSRM cannot answer (approximate).</summary>
    public static GeoLine Straight(GeoPoint from, GeoPoint to) => new([from, to]);

    /// <summary>Reads {"type":"LineString","coordinates":[[lon,lat],...]}.</summary>
    public static GeoLine Parse(string geoJson)
    {
        using var document = JsonDocument.Parse(geoJson);
        var coordinates = document.RootElement.GetProperty("coordinates");
        return new GeoLine(
            coordinates
                .EnumerateArray()
                .Select(c => new GeoPoint(c[0].GetDouble(), c[1].GetDouble()))
                .ToList()
        );
    }

    /// <summary>Writes the line as a GeoJSON LineString.</summary>
    public string ToGeoJson() =>
        new JsonObject
        {
            ["type"] = "LineString",
            ["coordinates"] = new JsonArray(
                Points.Select(p => (JsonNode)new JsonArray(p.Longitude, p.Latitude)).ToArray()
            ),
        }.ToJsonString();
}
