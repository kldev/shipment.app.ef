using Shipment.Domain.Common;

namespace Shipment.Domain.Carriers;

public enum ShiftStatus
{
    Offline,
    Available,
    Busy,
}

/// <summary>The data of a courier a form sends.</summary>
public sealed record CourierData(
    string DisplayName,
    string Phone,
    string Email,
    string VehicleType,
    string? VehiclePlate
);

/// <summary>
/// A courier with their vehicle, shift and last known position. Busy is set by dispatch (a shipment taken), never by
/// a person.
/// </summary>
public sealed class Courier : Audited
{
    private Courier() { }

    public Courier(Guid id, CourierData data)
    {
        Id = id;
        Set(data);
        ShiftStatus = ShiftStatus.Offline;
    }

    public Guid Id { get; private set; }

    public string DisplayName { get; private set; } = "";

    public string Phone { get; private set; } = "";

    public string Email { get; private set; } = "";

    public string VehicleType { get; private set; } = "";

    /// <summary>Registration plate: a car or a van has one, a bike never.</summary>
    public string? VehiclePlate { get; private set; }

    public ShiftStatus ShiftStatus { get; private set; }

    public DateTimeOffset? ShiftStartedAt { get; private set; }

    public decimal? LastLatitude { get; private set; }

    public decimal? LastLongitude { get; private set; }

    public DateTimeOffset? LastPositionAt { get; private set; }

    public bool Same(CourierData data) =>
        DisplayName == data.DisplayName
        && Phone == data.Phone
        && Email == data.Email
        && VehicleType == data.VehicleType
        && VehiclePlate == data.VehiclePlate;

    public Result<bool> Update(CourierData data)
    {
        if (ShiftStatus == ShiftStatus.Busy && VehicleType != data.VehicleType)
            return Error.Conflict(
                "The courier is delivering an order - change the vehicle after it."
            );
        if (Same(data))
            return false;
        Set(data);
        return true;
    }

    /// <summary>On shift (available); already on shift is a no-op. Documents are checked at assignment, not here.</summary>
    public bool StartShift(DateTimeOffset now)
    {
        if (ShiftStatus != ShiftStatus.Offline)
            return false;
        ShiftStatus = ShiftStatus.Available;
        ShiftStartedAt = now;
        return true;
    }

    public Result<bool> EndShift()
    {
        if (ShiftStatus == ShiftStatus.Busy)
            return Error.Conflict("The courier is delivering an order - end the shift after it.");
        if (ShiftStatus == ShiftStatus.Offline)
            return false;
        ShiftStatus = ShiftStatus.Offline;
        ShiftStartedAt = null;
        return true;
    }

    /// <summary>Dispatch takes the courier for a shipment; someone else may have taken them first.</summary>
    public Result TakeForShipment()
    {
        if (ShiftStatus != ShiftStatus.Available)
            return Error.Conflict("The courier is no longer available - choose another one.");
        ShiftStatus = ShiftStatus.Busy;
        return Result.Success;
    }

    /// <summary>The shipment ended or the courier was taken off it: available again.</summary>
    public void Release()
    {
        if (ShiftStatus == ShiftStatus.Busy)
            ShiftStatus = ShiftStatus.Available;
    }

    private void Set(CourierData data)
    {
        DisplayName = data.DisplayName;
        Phone = data.Phone;
        Email = data.Email;
        VehicleType = data.VehicleType;
        VehiclePlate = data.VehicleType == Carriers.VehicleType.Bike ? null : data.VehiclePlate;
    }
}
