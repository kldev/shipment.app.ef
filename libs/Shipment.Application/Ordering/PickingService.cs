using Microsoft.EntityFrameworkCore;
using Shipment.Application.Common;
using Shipment.Domain.Common;
using Shipment.Domain.Ordering;
using Shipment.Messaging.Contracts;
using Shipment.Persistence;

namespace Shipment.Application.Ordering;

/// <summary>
/// Picking (ordering): the store packs an order - the shelf may disagree with the catalog. The admin acts for the
/// store (ADR 0006) and is kept as the picker.
/// </summary>
public sealed class PickingService(
    IDbContextFactory<ShipmentDbContext> factory,
    TimeProvider time,
    ICurrentActor actor
)
{
    private const string OrderName = "Order";

    /// <summary>Orders placed or being picked, oldest first.</summary>
    public async Task<IReadOnlyList<PickingQueueRow>> QueueAsync(
        Guid? pickupPointId = null,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var orders = db.Orders.Where(o =>
            o.Status == OrderStatus.Placed || o.Status == OrderStatus.Picking
        );
        if (pickupPointId is { } point)
            orders = orders.Where(o => o.PickupPointId == point);
        var rows = await orders
            .OrderBy(o => o.PlacedAt)
            .ThenBy(o => o.Id)
            .Take(Lists.Max)
            .Select(o => new
            {
                o.Id,
                o.Code,
                o.Status,
                o.PartnerId,
                o.PartnerName,
                o.PickupPointId,
                o.PickupPointName,
                o.CustomerName,
                LineCount = o.Lines.Count,
                ItemCount = o.Lines.Sum(l => l.Quantity),
                AgeRestricted = o.Lines.Any(l => l.AgeRestricted),
                o.PlacedAt,
                o.PickingStartedAt,
                o.Version,
            })
            .ToListAsync(ct);
        var now = time.GetUtcNow();
        return rows.Select(r => new PickingQueueRow(
                r.Id,
                r.Code,
                r.Status,
                r.PartnerId,
                r.PartnerName,
                r.PickupPointId,
                r.PickupPointName,
                r.CustomerName,
                r.LineCount,
                r.ItemCount,
                r.AgeRestricted,
                r.PlacedAt,
                r.PickingStartedAt,
                (int)Math.Round((now - r.PlacedAt).TotalMinutes),
                r.Version
            ))
            .ToList();
    }

    /// <summary>
    /// Finished pickings (ready or cancelled at picking), whatever happened later, newest first; orders cancelled before
    /// picking are not here.
    /// </summary>
    public async Task<Slice<PickingHistoryRow>> HistoryAsync(
        PickingHistoryFilter? filter = null,
        int take = 50,
        CancellationToken ct = default
    )
    {
        filter ??= new PickingHistoryFilter();
        take = Math.Clamp(take, 1, Lists.Max);
        await using var db = await factory.CreateDbContextAsync(ct);
        var orders = db.Orders.Where(o =>
            o.PickingStartedAt != null && (o.ReadyAt ?? o.CancelledAt) != null
        );
        if (filter.PickupPointId is { } point)
            orders = orders.Where(o => o.PickupPointId == point);
        if (filter.From is { } from)
            orders = orders.Where(o => (o.ReadyAt ?? o.CancelledAt) >= from);
        if (filter.To is { } to)
            orders = orders.Where(o => (o.ReadyAt ?? o.CancelledAt) < to);
        if (filter.HasMissing is { } hasMissing)
            orders = orders.Where(o =>
                o.Lines.Any(l => (l.PickedQuantity ?? 0) < l.Quantity) == hasMissing
            );
        var rows = await orders
            .OrderByDescending(o => o.ReadyAt ?? o.CancelledAt)
            .ThenBy(o => o.Id)
            .Take(take + 1)
            .Select(o => new
            {
                o.Id,
                o.Code,
                o.Status,
                o.PartnerId,
                o.PartnerName,
                o.PickupPointId,
                o.PickupPointName,
                o.CustomerName,
                o.PickedBy,
                // ReSharper disable once AccessToDisposedClosure - an expression tree, translated to SQL before the context is disposed
                PickedByName = db
                    .Users.Where(u => u.Id == o.PickedBy)
                    .Select(u => u.DisplayName)
                    .FirstOrDefault(),
                o.PickingStartedAt,
                EndedAt = o.ReadyAt ?? o.CancelledAt,
                LineCount = o.Lines.Count,
                ItemCount = o.Lines.Sum(l => l.Quantity),
                ItemsPicked = o.Lines.Sum(l => l.PickedQuantity ?? 0),
                MissingLineCount = o.Lines.Count(l => (l.PickedQuantity ?? 0) < l.Quantity),
                o.GoodsOrderedMinor,
                o.GoodsPickedMinor,
                o.Version,
            })
            .ToListAsync(ct);
        return rows.Select(r => new PickingHistoryRow(
                r.Id,
                r.Code,
                r.Status,
                r.PartnerId,
                r.PartnerName,
                r.PickupPointId,
                r.PickupPointName,
                r.CustomerName,
                r.PickedBy,
                r.PickedByName,
                r.PickingStartedAt,
                r.EndedAt,
                r is { EndedAt: { } end, PickingStartedAt: { } start }
                    ? (int)Math.Round((end - start).TotalMinutes)
                    : 0,
                r.LineCount,
                r.ItemCount,
                r.ItemsPicked,
                r.ItemCount - r.ItemsPicked,
                r.MissingLineCount,
                r.MissingLineCount > 0,
                r.GoodsOrderedMinor,
                r.GoodsPickedMinor,
                Order.Currency,
                r.Version
            ))
            .ToList()
            .ToSlice(take);
    }

    /// <summary>placed -> picking; already picking is a no-op.</summary>
    public async Task<Result<uint>> StartAsync(StartPicking command, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.Orders.FindAsync([command.Id], ct);
        if (order is null)
            return Error.NotFound("Order not found.");
        if (!db.Expect(order, command.RowVersion))
            return Error.Stale(OrderName);
        var changed = order.StartPicking(actor.UserId, time.GetUtcNow());
        if (changed.Error is { } refused)
            return refused;
        if (!changed.Value)
            return order.Version;
        return await db.SaveAsync(OrderName, ct) is { Error: { } error } ? error : order.Version;
    }

    /// <summary>
    /// Packing done: every line decided once. Something packed -> ready and order-picked (billing captures and refunds
    /// the missing lines, dispatch sends a courier); nothing -> cancelled and order-cancelled. A missing product may leave
    /// the catalog at once.
    /// </summary>
    public async Task<Result<PickingFinished>> FinishAsync(
        FinishPicking command,
        CancellationToken ct = default
    )
    {
        // ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - a JSON body may carry null
        if (command.Lines is null || command.Lines.Any(l => l is null))
            return Error.Validation("Decide every line of the order once: packed or missing.");
        // ReSharper restore ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db
            .Orders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == command.Id, ct);
        if (order is null)
            return Error.NotFound("Order not found.");
        if (!db.Expect(order, command.RowVersion))
            return Error.Stale(OrderName);
        var now = time.GetUtcNow();
        var finished = order.FinishPicking(command.Lines, now);
        if (finished.Error is { } refused)
            return refused;

        var takeOff = command
            .Lines.Where(p => p.TakeOffCatalog)
            .Select(p => order.Lines.First(l => l.LineNo == p.LineNo))
            .Where(l => l.PickedQuantity < l.Quantity)
            .Select(l => l.ProductId)
            .ToList();
        if (takeOff.Count > 0)
            foreach (
                var product in await db.Products.Where(p => takeOff.Contains(p.Id)).ToListAsync(ct)
            )
                product.SetListed(false);

        if (finished.Value == OrderStatus.Cancelled)
            db.Enqueue(new OrderCancelled(order.Id, Order.NothingAvailable), now);
        else
            db.Enqueue(
                new OrderPicked(
                    order.Id,
                    order.Code,
                    order
                        .Lines.Where(l => l.PickedQuantity > 0)
                        .OrderBy(l => l.LineNo)
                        .Select(l => new PickedLine(l.LineNo, l.ProductId, l.PickedQuantity!.Value))
                        .ToList(),
                    order
                        .Lines.Where(l => l.PickedQuantity < l.Quantity)
                        .OrderBy(l => l.LineNo)
                        .Select(l => new PickedLine(
                            l.LineNo,
                            l.ProductId,
                            l.Quantity - l.PickedQuantity!.Value
                        ))
                        .ToList()
                ),
                now
            );
        return await db.SaveAsync(OrderName, ct) is { Error: { } error }
            ? error
            : new PickingFinished(order.Version, order.Status);
    }
}
