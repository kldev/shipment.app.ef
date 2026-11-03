namespace Shipment.Api.Components.Ui;

/// <summary>A point on <see cref="Map"/>. Kind picks the colour: pickup, customer, courier, muted.</summary>
public sealed record MapMarker(
    decimal Latitude,
    decimal Longitude,
    string Label,
    string Kind = "pickup",
    string? Popup = null
);

/// <summary>A line on <see cref="Map"/> - a route leg as (latitude, longitude) points. Kind: route, driven, approximate.</summary>
public sealed record MapLine(
    IReadOnlyList<(double Latitude, double Longitude)> Points,
    string Kind = "route"
);
