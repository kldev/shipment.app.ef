using Shipment.Domain.Common;
using Shipment.Domain.Crm;

namespace Shipment.Application.Common;

/// <summary>Checks shared by the forms of several contexts; each returns the tidy value or the refusal.</summary>
internal static class Inputs
{
    public static Result<Place> Place(
        string? addressLine,
        string? city,
        string? postcode,
        decimal latitude,
        decimal longitude
    )
    {
        var (line, town, code) = (Text.Clean(addressLine), Text.Clean(city), Text.Clean(postcode));
        if (line is null || town is null || code is null)
            return Error.Validation("Street, city and postcode are required.");
        var (lat, lon) = (Text.Coordinate(latitude), Text.Coordinate(longitude));
        if (!Text.IsPlace(lat, lon))
            return Error.Validation(
                "Pick the place on the map: latitude -90..90, longitude -180..180."
            );
        return new Place(line, town, code, lat, lon);
    }

    public static Result<string> Email(string? value)
    {
        var email = Text.Clean(value)?.ToLowerInvariant();
        return Text.IsEmail(email) ? email! : Error.Validation("Enter a valid e-mail address.");
    }

    /// <summary>Optional phone: null when blank.</summary>
    public static Result<string?> OptionalPhone(string? value)
    {
        var phone = Text.Clean(value);
        if (phone is not null && !Text.IsPhone(phone))
            return Error.Validation(
                "Phone may contain digits, spaces, brackets, dashes and a leading +."
            );
        return phone;
    }

    public static Error? Required(Guid id, string what) =>
        id == Guid.Empty ? Error.Validation($"{what} id is required.") : null;
}
