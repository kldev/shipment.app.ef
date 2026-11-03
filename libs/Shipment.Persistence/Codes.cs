using Microsoft.EntityFrameworkCore;

namespace Shipment.Persistence;

/// <summary>The next human code of an order or a shipment, from its sequence (gaps after a rollback are fine).</summary>
public static class Codes
{
    internal const string OrderSequence = "order_code_seq";
    internal const string ShipmentSequence = "shipment_code_seq";

    public static async Task<string> NextOrderCodeAsync(
        this ShipmentDbContext db,
        CancellationToken ct
    ) =>
        $"ORD-{await db.Database.SqlQuery<long>($"SELECT nextval('ordering.order_code_seq') AS \"Value\"").SingleAsync(ct)}";

    public static async Task<string> NextShipmentCodeAsync(
        this ShipmentDbContext db,
        CancellationToken ct
    ) =>
        $"SHP-{await db.Database.SqlQuery<long>($"SELECT nextval('dispatch.shipment_code_seq') AS \"Value\"").SingleAsync(ct):000000}";
}
