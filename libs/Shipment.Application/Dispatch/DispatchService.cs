using Microsoft.EntityFrameworkCore;
using Shipment.Application.Carriers;
using Shipment.Application.Common;
using Shipment.Application.Compliance;
using Shipment.Application.Ordering;
using Shipment.Domain.Carriers;
using Shipment.Domain.Common;
using Shipment.Domain.Dispatch;
using Shipment.Domain.Ordering;
using Shipment.Messaging.Contracts;
using Shipment.Persistence;

namespace Shipment.Application.Dispatch;

/// <summary>
/// The dispatch context: the shipment of a packed order and the courier who takes it. The runner creates it on
/// order-picked; the admin assigns a courier from the proposals and steps the shipment for the courier (ADR 0006).
/// The courier becomes busy and free again only through dispatch.
/// </summary>
public sealed class DispatchService(IDbContextFactory<ShipmentDbContext> factory, TimeProvider time)
{
    private const string ShipmentName = "Shipment";

    public async Task<IReadOnlyList<ShipmentRow>> ListShipmentsAsync(
        ShipmentFilter? filter = null,
        CancellationToken ct = default
    )
    {
        filter ??= new ShipmentFilter();
        await using var db = await factory.CreateDbContextAsync(ct);
        var shipments = db.Shipments.AsQueryable();
        if (filter.Status is { } status)
            shipments = shipments.Where(s => s.Status == status);
        if (filter.OrderId is { } order)
            shipments = shipments.Where(s => s.OrderId == order);
        if (filter.CourierId is { } courier)
            shipments = shipments.Where(s => s.CourierId == courier);
        return await RowsAsync(
            db,
            shipments.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id).Take(Lists.Max),
            ct
        );
    }

    public async Task<ShipmentRow?> GetShipmentAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await RowsAsync(db, db.Shipments.Where(s => s.Id == id), ct)).FirstOrDefault();
    }

    /// <summary>
    /// Who can take the shipment (docs/domain.md, "Courier choice"): on shift and available, cleared by compliance, on
    /// the required vehicle or a bigger one - nearest to the pickup point first (unknown position last). Empty once the
    /// shipment has a courier.
    /// </summary>
    public async Task<IReadOnlyList<ProposalRow>> ProposalsAsync(
        Guid shipmentId,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ProposalsAsync(db, shipmentId, ct);
    }

    private async Task<List<ProposalRow>> ProposalsAsync(
        ShipmentDbContext db,
        Guid shipmentId,
        CancellationToken ct
    )
    {
        var shipment = await db
            .Shipments.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == shipmentId, ct);
        if (shipment is not { Status: ShipmentStatus.Available })
            return [];
        var required = await db.VehicleTypes.FirstAsync(
            v => v.Code == shipment.RequiredVehicle,
            ct
        );
        var candidates = await db
            .Couriers.AsNoTracking()
            .Where(c => c.ShiftStatus == ShiftStatus.Available)
            .Join(db.VehicleTypes, c => c.VehicleType, v => v.Code, (c, v) => new { c, v })
            .Where(x => x.v.SortOrder >= required.SortOrder)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return [];

        var now = time.GetUtcNow();
        var cleared = (
            await ComplianceService.ClearanceAsync(
                db,
                candidates.Select(x => x.c.Id).ToList(),
                OperatorTime.Today(now),
                ct
            )
        )
            .Where(c => c.Cleared)
            .Select(c => c.CourierId)
            .ToHashSet();
        return candidates
            .Where(x => cleared.Contains(x.c.Id))
            .Select(x => new ProposalRow(
                x.c.Id,
                x.c.DisplayName,
                x.c.Phone,
                x.c.VehicleType,
                x.v.Name,
                x.c.VehiclePlate,
                x.c.LastLatitude,
                x.c.LastLongitude,
                CarriersService.Minutes(now, x.c.LastPositionAt),
                x.c is { LastLatitude: { } lat, LastLongitude: { } lon }
                    ? Distance.Metres(lat, lon, shipment.PickupLatitude, shipment.PickupLongitude)
                    : null
            ))
            .OrderBy(p => p.DistanceM ?? int.MaxValue)
            .ThenBy(p => p.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Gives an available shipment to a courier from the proposals - checked again here, the page's list may be old.
    /// Two admins taking the same courier at once: the second one is refused.
    /// </summary>
    public async Task<Result<uint>> AssignAsync(
        AssignCourier command,
        CancellationToken ct = default
    )
    {
        if (command.CourierId == Guid.Empty)
            return Error.Validation("Choose a courier.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var shipment = await db.Shipments.FindAsync([command.Id], ct);
        if (shipment is null)
            return Error.NotFound("Shipment not found.");
        if (!db.Expect(shipment, command.RowVersion))
            return Error.Stale(ShipmentName);
        if (shipment.Status != ShipmentStatus.Available)
            return Error.Conflict("The shipment already has a courier.");
        var courier = await db.Couriers.FindAsync([command.CourierId], ct);
        if (courier is null)
            return Error.NotFound("Courier not found.");
        if ((await ProposalsAsync(db, shipment.Id, ct)).All(p => p.CourierId != courier.Id))
            return Error.Conflict(
                "The courier cannot take this shipment now: not available, not cleared, or the vehicle is too small."
            );
        if (courier.TakeForShipment().Error is { } taken)
            return taken;
        var now = time.GetUtcNow();
        if (shipment.Assign(courier.Id, now).Error is { } refused)
            return refused;

        db.Enqueue(
            new CourierAssigned(shipment.Id, shipment.OrderId, shipment.Code, courier.Id),
            now
        );
        db.Enqueue(StatusChanged(shipment), now);
        var saved = await db.SaveAsync(ShipmentName, ct);
        // The courier's row moved under us: someone else took them first.
        if (saved.Error is { Kind: ErrorKind.Conflict })
            return Error.Conflict("The courier is no longer available - choose another one.");
        return saved.IsSuccess ? shipment.Version : saved.Error!;
    }

    /// <summary>The courier's next step; delivered lets the courier go and tells billing.</summary>
    public async Task<Result<uint>> AdvanceAsync(
        AdvanceShipment command,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var shipment = await db.Shipments.FindAsync([command.Id], ct);
        if (shipment is null)
            return Error.NotFound("Shipment not found.");
        if (!db.Expect(shipment, command.RowVersion))
            return Error.Stale(ShipmentName);
        var now = time.GetUtcNow();
        if (shipment.Advance(command.Status, command.AgeChecked, now).Error is { } refused)
            return refused;

        db.Enqueue(StatusChanged(shipment), now);
        if (shipment.Status == ShipmentStatus.Delivered)
        {
            await ReleaseAsync(db, shipment.CourierId, ct);
            db.Enqueue(
                new ShipmentDelivered(
                    shipment.Id,
                    shipment.OrderId,
                    shipment.Code,
                    shipment.CourierId!.Value
                ),
                now
            );
        }
        return await db.SaveAsync(ShipmentName, ct) is { Error: { } error }
            ? error
            : shipment.Version;
    }

    /// <summary>The delivery did not happen; it ends the order (billing refunds it). The courier is free again.</summary>
    public async Task<Result<uint>> FailAsync(FailShipment command, CancellationToken ct = default)
    {
        var reason = Text.Clean(command.Reason);
        if (reason is not { Length: <= 500 })
            return Error.Validation("Say why the delivery failed (up to 500 characters).");
        await using var db = await factory.CreateDbContextAsync(ct);
        var shipment = await db.Shipments.FindAsync([command.Id], ct);
        if (shipment is null)
            return Error.NotFound("Shipment not found.");
        if (!db.Expect(shipment, command.RowVersion))
            return Error.Stale(ShipmentName);
        var now = time.GetUtcNow();
        if (shipment.Fail(reason, now).Error is { } refused)
            return refused;
        await ReleaseAsync(db, shipment.CourierId, ct);
        db.Enqueue(StatusChanged(shipment, reason), now);
        return await db.SaveAsync(ShipmentName, ct) is { Error: { } error }
            ? error
            : shipment.Version;
    }

    /// <summary>Before the courier has the goods: the shipment is available again and the courier free.</summary>
    public async Task<Result<uint>> UnassignAsync(
        UnassignCourier command,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var shipment = await db.Shipments.FindAsync([command.Id], ct);
        if (shipment is null)
            return Error.NotFound("Shipment not found.");
        if (!db.Expect(shipment, command.RowVersion))
            return Error.Stale(ShipmentName);
        var courierId = shipment.CourierId;
        if (shipment.Unassign().Error is { } refused)
            return refused;
        await ReleaseAsync(db, courierId, ct);
        db.Enqueue(StatusChanged(shipment), time.GetUtcNow());
        return await db.SaveAsync(ShipmentName, ct) is { Error: { } error }
            ? error
            : shipment.Version;
    }

    /// <summary>
    /// The runner's reaction to order-picked (ADR 0007): a shipment for what the store packed, with the smallest vehicle
    /// type whose limits carry it (the biggest when none does - a person sees it). One per order: a repeated event
    /// does nothing; an order that is no longer ready gets none.
    /// </summary>
    public async Task<Result> CreateForOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Shipments.AnyAsync(s => s.OrderId == orderId, ct))
            return Result.Success;
        var order = await db
            .Orders.AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
            return Error.NotFound("Order not found.");
        if (order.Status != OrderStatus.Ready)
            return Result.Success;

        var freight = order.ToRow(time.GetUtcNow());
        var largest = freight.LargestSizeClass ?? SizeClass.S;
        var vehicles = await db.VehicleTypes.OrderBy(v => v.SortOrder).ToListAsync(ct);
        var vehicle =
            vehicles.FirstOrDefault(v => v.Carries(freight.WeightG, largest, freight.DistanceM))
            ?? vehicles[^1];

        db.Shipments.Add(
            new Delivery(
                Guid.CreateVersion7(),
                await db.NextShipmentCodeAsync(ct),
                new DeliveryTrip(
                    order.Id,
                    order.Code,
                    order.CustomerId,
                    order.CustomerName,
                    order.PartnerName,
                    order.PickupPointId,
                    order.PickupPointName,
                    order.PickupAddressLine,
                    order.PickupLatitude,
                    order.PickupLongitude,
                    order.AddressLabel,
                    order.AddressLine,
                    order.AddressCity,
                    order.AddressNotes,
                    order.AddressLatitude,
                    order.AddressLongitude,
                    order.DistanceM,
                    freight.ItemCount,
                    freight.WeightG,
                    largest,
                    freight.AgeRestricted
                ),
                vehicle.Code
            )
        );
        var saved = await db.SaveAsync(ShipmentName, ct);
        // Another runner created it at the same moment (unique order id): done all the same.
        return saved.Error is { Kind: ErrorKind.Conflict } ? Result.Success : saved;
    }

    private static async Task ReleaseAsync(
        ShipmentDbContext db,
        Guid? courierId,
        CancellationToken ct
    )
    {
        if (courierId is { } id && await db.Couriers.FindAsync([id], ct) is { } courier)
            courier.Release();
    }

    private static ShipmentStatusChanged StatusChanged(Delivery shipment, string? reason = null) =>
        new(shipment.Id, shipment.OrderId, shipment.Code, Wire.Name(shipment.Status), reason);

    private async Task<List<ShipmentRow>> RowsAsync(
        ShipmentDbContext db,
        IQueryable<Delivery> shipments,
        CancellationToken ct
    )
    {
        var rows = await shipments
            // A scalar subquery, not a join: the shipments keep the order the caller gave them.
            .Select(s => new
            {
                s,
                VehicleName = db
                    .VehicleTypes.Where(v => v.Code == s.RequiredVehicle)
                    .Select(v => v.Name)
                    .First(),
            })
            .ToListAsync(ct);
        var now = time.GetUtcNow();
        return rows.Select(x =>
            {
                var s = x.s;
                return new ShipmentRow(
                    s.Id,
                    s.Code,
                    s.Status,
                    s.OrderId,
                    s.OrderCode,
                    s.CustomerId,
                    s.CustomerName,
                    s.PartnerName,
                    s.PickupPointId,
                    s.PickupPointName,
                    s.PickupAddressLine,
                    s.PickupLatitude,
                    s.PickupLongitude,
                    s.DropoffLabel,
                    s.DropoffAddressLine,
                    s.DropoffCity,
                    s.DropoffNotes,
                    s.DropoffLatitude,
                    s.DropoffLongitude,
                    s.DistanceM,
                    s.ItemCount,
                    s.WeightG,
                    s.LargestSizeClass,
                    s.RequiredVehicle,
                    x.VehicleName,
                    s.AgeCheckRequired,
                    s.AgeChecked,
                    s.CourierId,
                    s.FailureReason,
                    s.CreatedAt,
                    s.AssignedAt,
                    s.ArrivedAtPickupAt,
                    s.PickedUpAt,
                    s.ArrivedAtCustomerAt,
                    s.DeliveredAt,
                    s.FailedAt,
                    (int)
                        Math.Round(
                            ((s.DeliveredAt ?? s.FailedAt ?? now) - s.CreatedAt).TotalMinutes
                        ),
                    s.Version
                );
            })
            .ToList();
    }
}
