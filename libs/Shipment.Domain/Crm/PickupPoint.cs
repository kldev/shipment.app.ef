using Shipment.Domain.Common;

namespace Shipment.Domain.Crm;

public enum PickupPointStatus
{
    Active,
    Closed,
}

/// <summary>
/// Where a partner hands orders over. Hours as minutes from the operator's midnight, the same every day; closing after
/// midnight is not modelled.
/// </summary>
public sealed class PickupPoint : Audited
{
    private PickupPoint() { }

    public PickupPoint(
        Guid id,
        Guid partnerId,
        string name,
        Place place,
        int opensMinute,
        int closesMinute
    )
    {
        Id = id;
        PartnerId = partnerId;
        Name = name;
        SetPlace(place);
        OpensMinute = opensMinute;
        ClosesMinute = closesMinute;
        Status = PickupPointStatus.Active;
    }

    public Guid Id { get; private set; }

    public Guid PartnerId { get; private set; }

    public Partner Partner { get; private set; } = null!;

    public string Name { get; private set; } = "";

    public string AddressLine { get; private set; } = "";

    public string City { get; private set; } = "";

    public string Postcode { get; private set; } = "";

    public decimal Latitude { get; private set; }

    public decimal Longitude { get; private set; }

    public int OpensMinute { get; private set; }

    public int ClosesMinute { get; private set; }

    public PickupPointStatus Status { get; private set; }

    public static bool ValidHours(int opensMinute, int closesMinute) =>
        opensMinute >= 0 && closesMinute <= 1440 && opensMinute < closesMinute;

    public bool Same(Guid partnerId, string name, Place place, int opensMinute, int closesMinute) =>
        PartnerId == partnerId
        && Name == name
        && SamePlace(place)
        && OpensMinute == opensMinute
        && ClosesMinute == closesMinute;

    public bool Update(
        string name,
        Place place,
        int opensMinute,
        int closesMinute,
        PickupPointStatus status
    )
    {
        if (Same(PartnerId, name, place, opensMinute, closesMinute) && Status == status)
            return false;
        Name = name;
        SetPlace(place);
        (OpensMinute, ClosesMinute, Status) = (opensMinute, closesMinute, status);
        return true;
    }

    private bool SamePlace(Place place) =>
        AddressLine == place.AddressLine
        && City == place.City
        && Postcode == place.Postcode
        && Latitude == place.Latitude
        && Longitude == place.Longitude;

    private void SetPlace(Place place) =>
        (AddressLine, City, Postcode, Latitude, Longitude) = (
            place.AddressLine,
            place.City,
            place.Postcode,
            place.Latitude,
            place.Longitude
        );
}
