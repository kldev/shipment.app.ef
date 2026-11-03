using System.Text.RegularExpressions;

namespace Shipment.Domain.Common;

/// <summary>How inputs are tidied and checked the same way everywhere (names, e-mails, phones, places).</summary>
public static partial class Text
{
    /// <summary>Trimmed; null for null, empty or blank.</summary>
    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static bool IsEmail(string? value) => value is not null && EmailPattern().IsMatch(value);

    public static bool IsPhone(string? value) => value is not null && PhonePattern().IsMatch(value);

    /// <summary>Latitude -90..90 and longitude -180..180, rounded to 6 places (numeric(9,6)).</summary>
    public static bool IsPlace(decimal latitude, decimal longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    public static decimal Coordinate(decimal value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\+?[0-9 ()-]{6,30}$")]
    private static partial Regex PhonePattern();
}
