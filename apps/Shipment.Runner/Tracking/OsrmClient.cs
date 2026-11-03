using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Shipment.Geo;

namespace Shipment.Runner.Tracking;

/// <summary>A leg of a route: its line, OSRM's distance and duration.</summary>
public sealed record RouteLeg(GeoLine Line, int DistanceM, int DurationS);

/// <summary>
/// OSRM's route service: GET /{profile path}/route/v1/driving/{lon,lat;lon,lat}?overview=full&amp;geometries=geojson
/// (coordinates longitude first). The resilience handler retries and times out (TrackingSetup); what is left throws.
/// A singleton over the named client: Wolverine builds its handlers from constructors and refuses typed clients
/// (a factory lambda is service location).
/// </summary>
public sealed class OsrmClient(IHttpClientFactory clients, IOptions<OsrmOptions> options)
{
    public const string HttpClientName = "osrm";

    public async Task<RouteLeg> RouteAsync(
        string profile,
        GeoPoint from,
        GeoPoint to,
        CancellationToken ct
    )
    {
        var settings = options.Value;
        var http = clients.CreateClient(HttpClientName);
        var path = profile == "bike" ? settings.BikePath : settings.CarPath;
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{path}/route/v1/driving/{from.Longitude},{from.Latitude};{to.Longitude},{to.Latitude}?overview=full&geometries=geojson"
        );
        using var document =
            await http.GetFromJsonAsync<JsonDocument>(url, ct)
            ?? throw new HttpRequestException("OSRM answered nothing.");
        var root = document.RootElement;
        if (root.GetProperty("code").GetString() != "Ok")
            throw new HttpRequestException($"OSRM: {root.GetProperty("code").GetString()}");
        var route = root.GetProperty("routes")[0];
        return new RouteLeg(
            GeoLine.Parse(route.GetProperty("geometry").GetRawText()),
            (int)Math.Round(route.GetProperty("distance").GetDouble()),
            (int)Math.Round(route.GetProperty("duration").GetDouble())
        );
    }
}
