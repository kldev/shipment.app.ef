using Shipment.Domain.Common;

namespace Shipment.Domain.Crm;

/// <summary>Where a customer wants orders; at most one is the default. Ordering copies it into an order.</summary>
public sealed class CustomerAddress : Audited
{
    private CustomerAddress() { }

    internal CustomerAddress(
        Guid id,
        Guid customerId,
        string label,
        Place place,
        string? notes,
        bool isDefault
    )
    {
        Id = id;
        CustomerId = customerId;
        Update(label, place, notes, isDefault);
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public string Label { get; private set; } = "";

    public string AddressLine { get; private set; } = "";

    public string City { get; private set; } = "";

    public string Postcode { get; private set; } = "";

    public decimal Latitude { get; private set; }

    public decimal Longitude { get; private set; }

    /// <summary>For the courier: floor, door code, where to leave it.</summary>
    public string? Notes { get; private set; }

    public bool IsDefault { get; private set; }

    public bool Same(string label, Place place, string? notes) =>
        Label == label
        && AddressLine == place.AddressLine
        && City == place.City
        && Postcode == place.Postcode
        && Latitude == place.Latitude
        && Longitude == place.Longitude
        && Notes == notes;

    internal void Update(string label, Place place, string? notes, bool isDefault)
    {
        Label = label;
        (AddressLine, City, Postcode, Latitude, Longitude) = (
            place.AddressLine,
            place.City,
            place.Postcode,
            place.Latitude,
            place.Longitude
        );
        Notes = notes;
        IsDefault = isDefault;
    }

    internal void SetDefault(bool isDefault) => IsDefault = isDefault;
}
