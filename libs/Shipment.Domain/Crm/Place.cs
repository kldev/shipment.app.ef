namespace Shipment.Domain.Crm;

/// <summary>A postal address with its place on the map (coordinates rounded to 6 places).</summary>
public sealed record Place(
    string AddressLine,
    string City,
    string Postcode,
    decimal Latitude,
    decimal Longitude
);
