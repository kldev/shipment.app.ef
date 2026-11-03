namespace Shipment.Api.Components.Layout;

/// <summary>A menu entry. Context = the bounded context (database module) the page belongs to, null for the shell.</summary>
public sealed record NavItem(string Icon, string Path, string Label, string? Context = null);

/// <summary>A group of the menu; Label null = no heading.</summary>
public sealed record NavGroup(string? Label, IReadOnlyList<NavItem> Items);

/// <summary>
/// The menu: one group per area of the business, one entry per bounded context (docs/domain.md). Everyone is an admin
/// for now (ADR 0006), so nothing is hidden by role.
/// </summary>
public static class NavItems
{
    public static readonly IReadOnlyList<NavGroup> Groups =
    [
        new(null, [new("layout-dashboard", "dashboard", "Dashboard")]),
        new(
            "Operations",
            [
                new("shopping-cart", "orders", "Orders", "ordering"),
                new("package-check", "picking", "Picking", "ordering"),
                new("package", "shipments", "Shipments", "dispatch"),
                new("map", "tracking", "Live tracking", "tracking"),
                new("bike", "couriers", "Couriers", "carriers"),
            ]
        ),
        new(
            "Network",
            [
                new("store", "partners", "Partners", "crm"),
                new("users", "customers", "Customers", "crm"),
            ]
        ),
        new(
            "Control",
            [
                new("shield-check", "compliance", "Compliance", "compliance"),
                new("receipt", "billing", "Billing", "billing"),
            ]
        ),
        new("Admin", [new("user-round", "users", "Users", "platform")]),
    ];
}
