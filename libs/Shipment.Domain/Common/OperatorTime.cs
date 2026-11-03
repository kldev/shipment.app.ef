namespace Shipment.Domain.Common;

/// <summary>
/// The operator's wall clock: one city, one zone (PRODUCT.md, "Not yet": a zone per city). Days of documents and of
/// billing periods, opening hours of pickup points are in this zone; instants are UTC everywhere else.
/// </summary>
public static class OperatorTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    private static DateTimeOffset Local(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Zone);

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(Local(now).DateTime);

    /// <summary>Minutes after the operator's midnight.</summary>
    public static int MinuteOfDay(DateTimeOffset now)
    {
        var local = Local(now);
        return local.Hour * 60 + local.Minute;
    }

    /// <summary>The instant the operator's day starts (midnight in the zone), in UTC.</summary>
    public static DateTimeOffset StartOf(DateOnly day)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, Zone.GetUtcOffset(midnight)).ToUniversalTime();
    }
}
