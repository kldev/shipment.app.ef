using System.Globalization;
using Shipment.Domain.Common;
using Shipment.Domain.Compliance;
using Shipment.Domain.Crm;
using Shipment.Domain.Dispatch;
using Shipment.Domain.Ordering;

namespace Shipment.Api.Web;

/// <summary>Labels of codes, dates and times as the pages show them (English, one market for now).</summary>
public static class Labels
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>
    /// The wall clock of the operator. Pages render on the server (the container runs in UTC), so local time is never
    /// the machine's: it is this zone. One zone for now (PRODUCT.md, "Not yet": a zone per city).
    /// </summary>
    private static readonly TimeZoneInfo Zone = OperatorTime.Zone;

    private static DateTime Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Zone).DateTime;

    /// <summary>An enum value as stored and sent: PartnerStatus.Paused is "paused", CourierAssigned "courier_assigned".</summary>
    public static string Wire(Enum value) =>
        System
            .Text.RegularExpressions.Regex.Replace(value.ToString(), "([a-z0-9])([A-Z])", "$1_$2")
            .ToLowerInvariant();

    public static string Role(string? role) =>
        role switch
        {
            Roles.Admin => "Admin",
            Roles.Dispatcher => "Dispatcher",
            Roles.Courier => "Courier",
            _ => role ?? "",
        };

    public static string Initials(string? name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpper(Culture),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpper(Culture),
        };
    }

    public static string FirstName(string? name) =>
        (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    /// <summary>"29 Oct 2026, 14:05" in the operator's zone.</summary>
    public static string DateTime(DateTimeOffset? at) =>
        at is { } value ? Local(value).ToString("d MMM yyyy, HH:mm", Culture) : "";

    /// <summary>Today's date in the operator's zone (the Billing page's default period).</summary>
    public static DateTime Today(TimeProvider? clock = null) =>
        Local((clock ?? TimeProvider.System).GetUtcNow()).Date;

    /// <summary>"14:05" in the operator's zone.</summary>
    public static string Time(DateTimeOffset? at) =>
        at is { } value ? Local(value).ToString("HH:mm", Culture) : "";

    /// <summary>Midnight of today in the operator's zone - the start of "today" in filters.</summary>
    public static DateTimeOffset TodayStart(TimeProvider? clock = null)
    {
        var now = TimeZoneInfo.ConvertTime((clock ?? TimeProvider.System).GetUtcNow(), Zone);
        return new DateTimeOffset(now.Date, now.Offset);
    }

    /// <summary>"12 min ago"; older than a day: the date.</summary>
    public static string Ago(DateTimeOffset? when, TimeProvider? clock = null)
    {
        if (when is not { } at)
            return "";
        var span = (clock ?? TimeProvider.System).GetUtcNow() - at;
        return span.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)span.TotalMinutes} min ago",
            < 60 * 24 => $"{(int)span.TotalHours} h ago",
            _ => DateTime(at),
        };
    }

    /// <summary>Partner kinds (crm.partners.kind) with their names and icons, in the order of the form.</summary>
    public static readonly IReadOnlyList<(
        PartnerKind Value,
        string Name,
        string Icon
    )> PartnerKinds =
    [
        (Domain.Crm.PartnerKind.Grocery, "Grocery", "shopping-basket"),
        (Domain.Crm.PartnerKind.Restaurant, "Restaurant", "utensils"),
        (Domain.Crm.PartnerKind.Pharmacy, "Pharmacy", "pill"),
        (Domain.Crm.PartnerKind.Shop, "Shop", "gift"),
    ];

    public static string PartnerKind(PartnerKind kind) =>
        PartnerKinds.First(k => k.Value == kind).Name;

    public static string PartnerIcon(PartnerKind kind) =>
        PartnerKinds.First(k => k.Value == kind).Icon;

    /// <summary>Size classes of products (catalog.products.size_class) - they choose the vehicle later.</summary>
    public static readonly IReadOnlyList<(SizeClass Value, string Name)> SizeClasses =
    [
        (SizeClass.S, "S - fits a backpack"),
        (SizeClass.M, "M - a bag or a box"),
        (SizeClass.L, "L - a big box"),
        (SizeClass.XL, "XL - needs a van"),
    ];

    /// <summary>"8:00" from minutes after midnight (pickup points' hours).</summary>
    public static string Clock(int? minutes) => minutes is { } m ? $"{m / 60}:{m % 60:00}" : "";

    public static TimeOnly ToTime(int? minutes) =>
        minutes is { } m and < 1440
            ? TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(m))
            : new TimeOnly(23, 59);

    public static int FromTime(TimeOnly time) => time.Hour * 60 + time.Minute;

    /// <summary>"15 %" from basis points.</summary>
    public static string Percent(int? basisPoints) =>
        basisPoints is { } bps
            ? $"{(bps / 100m).ToString("0.##", CultureInfo.InvariantCulture)} %"
            : "";

    /// <summary>"0.9 kg" or "350 g".</summary>
    public static string Weight(int? grams) =>
        grams switch
        {
            null => "",
            < 1000 => $"{grams} g",
            _ => $"{(grams.Value / 1000m).ToString("0.##", CultureInfo.InvariantCulture)} kg",
        };

    /// <summary>"fruit_veg" as "Fruit veg".</summary>
    public static string Category(string? category) =>
        string.IsNullOrEmpty(category)
            ? ""
            : char.ToUpper(category[0], Culture) + category[1..].Replace('_', ' ');

    /// <summary>Vehicle types (carriers.vehicle_types) - name and icon.</summary>
    public static string VehicleIcon(string? vehicleType) =>
        vehicleType switch
        {
            "bike" => "bike",
            "car" => "car",
            _ => "truck",
        };

    /// <summary>Courier documents (compliance.courier_documents.kind), in the order of the form.</summary>
    public static readonly IReadOnlyList<(DocumentKind Value, string Name)> DocumentKinds =
    [
        (Domain.Compliance.DocumentKind.IdCard, "ID card"),
        (Domain.Compliance.DocumentKind.DrivingLicence, "Driving licence"),
        (Domain.Compliance.DocumentKind.VehicleInsurance, "Vehicle insurance"),
        (Domain.Compliance.DocumentKind.FoodHygiene, "Food hygiene certificate"),
    ];

    public static string DocumentKind(DocumentKind kind) =>
        DocumentKinds.First(k => k.Value == kind).Name;

    /// <summary>"29 Oct 2026".</summary>
    public static string Date(DateOnly? date) =>
        date is { } d ? d.ToString("d MMM yyyy", Culture) : "";

    /// <summary>"PLN 12.50" - an amount in minor units (grosze, cents).</summary>
    public static string Money(long? minor, string? currency) =>
        minor is { } value
            ? $"{currency} {(value / 100m).ToString("0.00", CultureInfo.InvariantCulture)}"
            : "–";

    /// <summary>Order statuses (ordering.orders.status) in the order of the lifecycle, with the badge style.</summary>
    public static readonly IReadOnlyList<(
        OrderStatus Value,
        string Name,
        string Badge
    )> OrderStatuses =
    [
        (Domain.Ordering.OrderStatus.Placed, "Placed", "badge-primary"),
        (Domain.Ordering.OrderStatus.Picking, "Picking", "badge-warning"),
        (Domain.Ordering.OrderStatus.Ready, "Ready", "badge-accent"),
        (Domain.Ordering.OrderStatus.Delivered, "Delivered", "badge-success"),
        (Domain.Ordering.OrderStatus.Failed, "Failed", "badge-destructive"),
        (Domain.Ordering.OrderStatus.Cancelled, "Cancelled", "badge-secondary"),
    ];

    public static string OrderStatus(OrderStatus status) =>
        OrderStatuses.First(s => s.Value == status).Name;

    /// <summary>"850 m" or "2.4 km" - a straight-line distance.</summary>
    public static string Distance(int? metres) =>
        metres switch
        {
            null => "",
            < 1000 => $"{metres} m",
            _ => $"{(metres.Value / 1000m).ToString("0.0", CultureInfo.InvariantCulture)} km",
        };

    /// <summary>Shipment statuses (dispatch.shipments.status) in the order of the lifecycle, with the badge style.</summary>
    public static readonly IReadOnlyList<(
        ShipmentStatus Value,
        string Name,
        string Badge
    )> ShipmentStatuses =
    [
        (Domain.Dispatch.ShipmentStatus.Available, "Available", "badge-primary"),
        (Domain.Dispatch.ShipmentStatus.CourierAssigned, "Courier assigned", "badge-warning"),
        (Domain.Dispatch.ShipmentStatus.ArrivedAtPickup, "At pickup", "badge-warning"),
        (Domain.Dispatch.ShipmentStatus.PickedUp, "Picked up", "badge-accent"),
        (Domain.Dispatch.ShipmentStatus.ArrivedAtCustomer, "At customer", "badge-accent"),
        (Domain.Dispatch.ShipmentStatus.Delivered, "Delivered", "badge-success"),
        (Domain.Dispatch.ShipmentStatus.Failed, "Failed", "badge-destructive"),
    ];

    public static string ShipmentStatus(ShipmentStatus status) =>
        ShipmentStatuses.First(s => s.Value == status).Name;

    /// <summary>The courier's next step after a status, and the button that does it; null at the end.</summary>
    public static (ShipmentStatus Status, string Action)? NextStep(ShipmentStatus status) =>
        status switch
        {
            Domain.Dispatch.ShipmentStatus.CourierAssigned => (
                Domain.Dispatch.ShipmentStatus.ArrivedAtPickup,
                "Arrived at pickup"
            ),
            Domain.Dispatch.ShipmentStatus.ArrivedAtPickup => (
                Domain.Dispatch.ShipmentStatus.PickedUp,
                "Picked up"
            ),
            Domain.Dispatch.ShipmentStatus.PickedUp => (
                Domain.Dispatch.ShipmentStatus.ArrivedAtCustomer,
                "Arrived at customer"
            ),
            Domain.Dispatch.ShipmentStatus.ArrivedAtCustomer => (
                Domain.Dispatch.ShipmentStatus.Delivered,
                "Delivered"
            ),
            _ => null,
        };
}
