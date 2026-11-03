using Shipment.Domain.Common;

namespace Shipment.Domain.Compliance;

public enum DocumentKind
{
    IdCard,
    DrivingLicence,
    VehicleInsurance,
    FoodHygiene,
}

/// <summary>A document's state on a day of the operator: expired wins over unverified, expiring = within 30 days.</summary>
public enum DocumentState
{
    Valid,
    Expiring,
    Unverified,
    Expired,
}

/// <summary>
/// A courier's current document of a kind - one per kind, replaced when renewed. It counts once an admin verified it
/// and while it is valid.
/// </summary>
public sealed class CourierDocument : Audited
{
    public const int ExpiringDays = 30;

    private CourierDocument() { }

    public CourierDocument(
        Guid id,
        Guid courierId,
        DocumentKind kind,
        string number,
        DateOnly validUntil
    )
    {
        Id = id;
        CourierId = courierId;
        Kind = kind;
        Number = number;
        ValidUntil = validUntil;
    }

    public Guid Id { get; private set; }

    public Guid CourierId { get; private set; }

    public DocumentKind Kind { get; private set; }

    public string Number { get; private set; } = "";

    public DateOnly ValidUntil { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public Guid? VerifiedBy { get; private set; }

    /// <summary>A renewal: another number or date needs verifying again. The same values are a no-op.</summary>
    public bool Renew(string number, DateOnly validUntil)
    {
        if (Number == number && ValidUntil == validUntil)
            return false;
        (Number, ValidUntil) = (number, validUntil);
        (VerifiedAt, VerifiedBy) = (null, null);
        return true;
    }

    /// <summary>An admin saw it; already verified is a no-op.</summary>
    public bool Verify(Guid? by, DateTimeOffset now)
    {
        if (VerifiedAt is not null)
            return false;
        (VerifiedAt, VerifiedBy) = (now, by);
        return true;
    }

    public static DocumentState StateOf(DateOnly validUntil, bool verified, DateOnly today) =>
        validUntil < today ? DocumentState.Expired
        : !verified ? DocumentState.Unverified
        : validUntil < today.AddDays(ExpiringDays) ? DocumentState.Expiring
        : DocumentState.Valid;
}
