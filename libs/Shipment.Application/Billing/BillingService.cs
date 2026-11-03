using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Shipment.Domain.Billing;
using Shipment.Domain.Common;
using Shipment.Persistence;

namespace Shipment.Application.Billing;

/// <summary>
/// The billing context: the money of an order, simulated (no payment provider), in an append-only ledger. Its entries
/// come from the runner's reactions (ADR 0007); each event id is kept, and one entry of a kind per order (and line)
/// stops a second booking even from another event.
/// </summary>
public sealed class BillingService(IDbContextFactory<ShipmentDbContext> factory, TimeProvider time)
{
    private static readonly EntryKind[] Captures =
    [
        EntryKind.CaptureGoods,
        EntryKind.CaptureFee,
        EntryKind.CaptureTip,
    ];

    // ---- Queries ----

    public async Task<IReadOnlyList<EntryRow>> ListEntriesAsync(
        EntryFilter? filter = null,
        CancellationToken ct = default
    )
    {
        filter ??= new EntryFilter();
        await using var db = await factory.CreateDbContextAsync(ct);
        var entries = db.LedgerEntries.AsQueryable();
        if (filter.OrderId is { } order)
            entries = entries.Where(e => e.OrderId == order);
        if (filter.ShipmentId is { } shipment)
            entries = entries.Where(e => e.ShipmentId == shipment);
        if (filter.PartnerId is { } partner)
            entries = entries.Where(e => e.PartnerId == partner);
        if (filter.CourierId is { } courier)
            entries = entries.Where(e => e.CourierId == courier);
        if (filter.Kind is { } kind)
            entries = entries.Where(e => e.Kind == kind);
        return await entries
            .OrderBy(e => e.Id)
            .Take(1000)
            .Select(e => new EntryRow(
                e.Id,
                e.Kind,
                e.OrderId,
                e.OrderCode,
                e.PartnerId,
                e.ShipmentId,
                e.CourierId,
                e.LineNo,
                e.ProductId,
                e.Quantity,
                e.Basis,
                e.AmountMinor,
                LedgerEntry.Currency,
                e.Description,
                e.BookedAt
            ))
            .ToListAsync(ct);
    }

    public async Task<OrderBillingRow?> GetOrderBillingAsync(
        Guid orderId,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entries = await db
            .LedgerEntries.AsNoTracking()
            .Where(e => e.OrderId == orderId)
            .ToListAsync(ct);
        return entries.Count == 0 ? null : OrderBilling(entries);
    }

    public async Task<IReadOnlyList<DailySummaryRow>> DailySummaryAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    )
    {
        var entries = await BookedAsync(from, to, null, ct);
        return entries
            .GroupBy(e => OperatorTime.Today(e.BookedAt))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                long Sum(params EntryKind[] kinds) =>
                    g.Where(e => kinds.Contains(e.Kind)).Sum(e => e.AmountMinor);
                return new DailySummaryRow(
                    g.Key,
                    g.Count(e => e.Kind == EntryKind.Authorization),
                    Sum(Captures),
                    Sum(EntryKind.Refund),
                    Sum(EntryKind.Commission),
                    Sum(EntryKind.Payout),
                    Sum(EntryKind.CaptureFee, EntryKind.Commission) - Sum(EntryKind.Payout),
                    Sum(EntryKind.Tip),
                    LedgerEntry.Currency
                );
            })
            .ToList();
    }

    /// <summary>What each partner earned in the period (by delivery): packed goods, commission and what it is owed.</summary>
    public async Task<IReadOnlyList<PartnerBalanceRow>> PartnerBalancesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    )
    {
        var commissions = await BookedAsync(from, to, EntryKind.Commission, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var ids = commissions.Select(e => e.PartnerId).Distinct().ToList();
        var partners = await db
            .Partners.Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        return commissions
            .GroupBy(e => e.PartnerId)
            .Select(g =>
            {
                var partner = partners.GetValueOrDefault(g.Key);
                var goods = g.Sum(e => e.Basis ?? 0);
                var commission = g.Sum(e => e.AmountMinor);
                return new PartnerBalanceRow(
                    g.Key,
                    partner?.Name,
                    partner?.CommissionBps,
                    g.Count(),
                    goods,
                    commission,
                    goods - commission,
                    LedgerEntry.Currency
                );
            })
            .OrderByDescending(p => p.PayableMinor)
            .ToList();
    }

    /// <summary>What each courier earned in the period: deliveries, kilometres, payout, and the tips customers left.</summary>
    public async Task<IReadOnlyList<CourierPayoutRow>> CourierPayoutsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    )
    {
        var entries = await BookedAsync(from, to, null, ct);
        var payouts = entries
            .Where(e => e is { Kind: EntryKind.Payout, CourierId: not null })
            .ToList();
        var ids = payouts.Select(e => e.CourierId!.Value).Distinct().ToList();
        await using var db = await factory.CreateDbContextAsync(ct);
        var couriers = await db
            .Couriers.Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);
        var rates = await db.PayoutRates.ToDictionaryAsync(r => r.VehicleType, ct);
        return payouts
            .GroupBy(e => e.CourierId!.Value)
            .Select(g =>
            {
                var courier = couriers.GetValueOrDefault(g.Key);
                var rate = courier is null ? null : rates.GetValueOrDefault(courier.VehicleType);
                return new CourierPayoutRow(
                    g.Key,
                    courier?.DisplayName,
                    courier?.VehicleType,
                    g.Count(),
                    g.Sum(e => e.Basis ?? 0),
                    g.Sum(e => e.AmountMinor),
                    rate?.BaseMinor,
                    rate?.PerKmMinor,
                    entries
                        .Where(e => e.Kind == EntryKind.Tip && e.CourierId == g.Key)
                        .Sum(e => e.AmountMinor),
                    LedgerEntry.Currency
                );
            })
            .OrderByDescending(c => c.PayoutMinor)
            .ToList();
    }

    public async Task<IReadOnlyList<PayoutRateRow>> ListPayoutRatesAsync(
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db
            .PayoutRates.Join(
                db.VehicleTypes,
                r => r.VehicleType,
                v => v.Code,
                (r, v) => new { r, v.SortOrder }
            )
            .OrderBy(x => x.SortOrder)
            .Select(x => new PayoutRateRow(
                x.r.VehicleType,
                x.r.BaseMinor,
                x.r.PerKmMinor,
                LedgerEntry.Currency
            ))
            .ToListAsync(ct);
    }

    // ---- The runner's reactions (ADR 0007) ----

    /// <summary>order-placed: the customer's payment authorized for the total - goods as ordered, fee and tip.</summary>
    public Task<Result> AuthorizeAsync(
        Guid eventId,
        Guid orderId,
        CancellationToken ct = default
    ) =>
        BookAsync(
            eventId,
            "order-placed",
            orderId,
            (db, order, entries, now) =>
            {
                Book(entries, db, Authorization(eventId, order, now));
                return Task.CompletedTask;
            },
            ct
        );

    /// <summary>
    /// order-picked: the customer pays for what was packed - captures of the goods, the fee and the tip (missing items
    /// do not lower it) - and every missing line is refunded (unit price x missing).
    /// </summary>
    public Task<Result> SettleAsync(Guid eventId, Guid orderId, CancellationToken ct = default) =>
        BookAsync(
            eventId,
            "order-picked",
            orderId,
            async (db, order, entries, now) =>
            {
                // Should order-placed have been lost, the authorization comes first all the same.
                Book(entries, db, Authorization(eventId, order, now));
                Book(
                    entries,
                    db,
                    Entry(
                        eventId,
                        EntryKind.CaptureGoods,
                        order,
                        order.GoodsPickedMinor ?? 0,
                        "Packed goods charged",
                        now
                    )
                );
                Book(
                    entries,
                    db,
                    Entry(
                        eventId,
                        EntryKind.CaptureFee,
                        order,
                        order.DeliveryFeeMinor,
                        "Delivery fee charged",
                        now
                    )
                );
                if (order.TipMinor > 0)
                    Book(
                        entries,
                        db,
                        Entry(
                            eventId,
                            EntryKind.CaptureTip,
                            order,
                            order.TipMinor,
                            "Tip for the courier charged",
                            now
                        )
                    );
                var missing = await db
                    .OrderLines.Where(l => l.OrderId == order.Id && l.PickedQuantity < l.Quantity)
                    .OrderBy(l => l.LineNo)
                    .ToListAsync(ct);
                foreach (var line in missing)
                {
                    var quantity = line.Quantity - line.PickedQuantity!.Value;
                    Book(
                        entries,
                        db,
                        Entry(
                                eventId,
                                EntryKind.Refund,
                                order,
                                line.UnitPriceMinor * quantity,
                                $"Missing: {quantity} x {line.Name}",
                                now
                            )
                            .ForLine(line.LineNo, line.ProductId, quantity)
                    );
                }
            },
            ct
        );

    /// <summary>
    /// order-cancelled or a failed delivery: everything back - what was captured (packed goods, fee and tip) or, before
    /// any capture, what was authorized. One full refund per order.
    /// </summary>
    public Task<Result> RefundAsync(
        Guid eventId,
        string messageType,
        Guid orderId,
        string? reason,
        CancellationToken ct = default
    ) =>
        BookAsync(
            eventId,
            messageType,
            orderId,
            (db, order, entries, now) =>
            {
                var captured = entries.Where(e => Captures.Contains(e.Kind)).ToList();
                var authorized = entries.Where(e => e.Kind == EntryKind.Authorization).ToList();
                var amount =
                    captured.Count > 0 ? captured.Sum(e => e.AmountMinor)
                    : authorized.Count > 0 ? authorized.Sum(e => e.AmountMinor)
                    : order.GoodsOrderedMinor + order.DeliveryFeeMinor + order.TipMinor;
                Book(
                    entries,
                    db,
                    Entry(
                        eventId,
                        EntryKind.Refund,
                        order,
                        amount,
                        $"Refunded in full: {Text.Clean(reason) ?? "order ended"}",
                        now
                    )
                );
                return Task.CompletedTask;
            },
            ct
        );

    /// <summary>
    /// shipment-delivered: the partner's commission on the packed goods (the fee is the operator's), the courier's payout
    /// - the vehicle's base plus per km of the route (straight-line distance without a route) - and the tip, all of it,
    /// to the courier.
    /// </summary>
    public async Task<Result> SettleDeliveryAsync(
        Guid eventId,
        Guid shipmentId,
        CancellationToken ct = default
    )
    {
        await using var lookup = await factory.CreateDbContextAsync(ct);
        var shipment = await lookup
            .Shipments.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == shipmentId, ct);
        if (shipment is null)
            return Error.NotFound("Shipment not found.");
        return await BookAsync(
            eventId,
            "shipment-delivered",
            shipment.OrderId,
            async (db, order, entries, now) =>
            {
                var bps =
                    await db
                        .Partners.Where(p => p.Id == order.PartnerId)
                        .Select(p => (int?)p.CommissionBps)
                        .FirstOrDefaultAsync(ct)
                    ?? 0;
                var rate = await db
                    .Couriers.Where(c => c.Id == shipment.CourierId)
                    .Join(db.PayoutRates, c => c.VehicleType, r => r.VehicleType, (_, r) => r)
                    .FirstOrDefaultAsync(ct);
                var metres =
                    await db
                        .Routes.Where(r => r.ShipmentId == shipmentId)
                        .Select(r => (long?)(r.ToPickupDistanceM + r.ToCustomerDistanceM))
                        .FirstOrDefaultAsync(ct)
                    ?? shipment.DistanceM;
                var goods = order.GoodsPickedMinor ?? order.GoodsOrderedMinor;

                Book(
                    entries,
                    db,
                    Entry(
                            eventId,
                            EntryKind.Commission,
                            order,
                            (long)Math.Round(goods * bps / 10000m, MidpointRounding.AwayFromZero),
                            $"Commission {(bps / 100m).ToString("0.##", CultureInfo.InvariantCulture)} % on the packed goods",
                            now
                        )
                        .ForDelivery(shipmentId, null, goods)
                );
                Book(
                    entries,
                    db,
                    Entry(
                            eventId,
                            EntryKind.Payout,
                            order,
                            rate?.PayoutFor(metres) ?? 0,
                            $"Payout: {rate?.VehicleType ?? "vehicle"}, {(metres / 1000m).ToString("0.0", CultureInfo.InvariantCulture)} km",
                            now
                        )
                        .ForDelivery(shipmentId, shipment.CourierId, metres)
                );
                if (order.TipMinor > 0)
                    Book(
                        entries,
                        db,
                        Entry(
                                eventId,
                                EntryKind.Tip,
                                order,
                                order.TipMinor,
                                "Tip from the customer",
                                now
                            )
                            .ForDelivery(shipmentId, shipment.CourierId, null)
                    );
            },
            ct
        );
    }

    /// <summary>
    /// One reaction in one save: the event marked processed and its entries. The same event again does nothing; a
    /// booking that races another one fails on the unique keys, and the runner's retry then finds it done.
    /// </summary>
    private async Task<Result> BookAsync(
        Guid eventId,
        string messageType,
        Guid orderId,
        Func<
            ShipmentDbContext,
            Domain.Ordering.Order,
            List<LedgerEntry>,
            DateTimeOffset,
            Task
        > book,
        CancellationToken ct
    )
    {
        // Two deliveries of one event, or two events booking the same kind, at the same moment: the second save hits a
        // unique key. Booking again in a fresh context sees what the first one saved and books nothing more - the
        // reaction stays a no-op instead of a refusal the runner would retry.
        var result = await BookOnceAsync(eventId, messageType, orderId, book, ct);
        return result.Error is { Kind: ErrorKind.Conflict }
            ? await BookOnceAsync(eventId, messageType, orderId, book, ct)
            : result;
    }

    private async Task<Result> BookOnceAsync(
        Guid eventId,
        string messageType,
        Guid orderId,
        Func<
            ShipmentDbContext,
            Domain.Ordering.Order,
            List<LedgerEntry>,
            DateTimeOffset,
            Task
        > book,
        CancellationToken ct
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.ProcessedEvents.AnyAsync(e => e.EventId == eventId, ct))
            return Result.Success;
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
            return Error.NotFound("Order not found.");
        var now = time.GetUtcNow();
        db.ProcessedEvents.Add(
            new ProcessedEvent
            {
                EventId = eventId,
                MessageType = messageType,
                ProcessedAt = now,
            }
        );
        var entries = await db.LedgerEntries.Where(e => e.OrderId == orderId).ToListAsync(ct);
        await book(db, order, entries, now);
        return await db.SaveAsync("Ledger", ct);
    }

    /// <summary>Adds the entry unless the order already has one of its kind (or of its refunded line).</summary>
    private static void Book(List<LedgerEntry> entries, ShipmentDbContext db, LedgerEntry entry)
    {
        var taken = entry.Kind switch
        {
            EntryKind.Refund when entry.LineNo is { } line => entries.Any(e =>
                e.Kind == EntryKind.Refund && e.LineNo == line
            ),
            EntryKind.Refund => entries.Any(e => e is { Kind: EntryKind.Refund, LineNo: null }),
            _ => entries.Any(e => e.Kind == entry.Kind),
        };
        if (taken)
            return;
        entries.Add(entry);
        db.LedgerEntries.Add(entry);
    }

    private static LedgerEntry Authorization(
        Guid eventId,
        Domain.Ordering.Order order,
        DateTimeOffset now
    ) =>
        Entry(
            eventId,
            EntryKind.Authorization,
            order,
            order.GoodsOrderedMinor + order.DeliveryFeeMinor + order.TipMinor,
            "Payment authorized for the order",
            now
        );

    private static LedgerEntry Entry(
        Guid eventId,
        EntryKind kind,
        Domain.Ordering.Order order,
        long amountMinor,
        string description,
        DateTimeOffset now
    ) => new(eventId, kind, order.Id, order.Code, order.PartnerId, amountMinor, description, now);

    private async Task<List<LedgerEntry>> BookedAsync(
        DateOnly from,
        DateOnly to,
        EntryKind? kind,
        CancellationToken ct
    )
    {
        var (start, end) = (OperatorTime.StartOf(from), OperatorTime.StartOf(to.AddDays(1)));
        await using var db = await factory.CreateDbContextAsync(ct);
        var entries = db
            .LedgerEntries.AsNoTracking()
            .Where(e => e.BookedAt >= start && e.BookedAt < end);
        if (kind is { } wanted)
            entries = entries.Where(e => e.Kind == wanted);
        return await entries.ToListAsync(ct);
    }

    private static OrderBillingRow OrderBilling(List<LedgerEntry> entries)
    {
        long Sum(EntryKind kind) => entries.Where(e => e.Kind == kind).Sum(e => e.AmountMinor);
        var first = entries.MinBy(e => e.Id)!;
        var commissionBasis = entries
            .Where(e => e.Kind == EntryKind.Commission)
            .Sum(e => e.Basis ?? 0);
        return new OrderBillingRow(
            first.OrderId,
            first.OrderCode,
            first.PartnerId,
            Sum(EntryKind.Authorization),
            Sum(EntryKind.CaptureGoods),
            Sum(EntryKind.CaptureFee),
            Sum(EntryKind.CaptureTip),
            Sum(EntryKind.Refund),
            Sum(EntryKind.Authorization) - Sum(EntryKind.Refund),
            Sum(EntryKind.Commission),
            commissionBasis - Sum(EntryKind.Commission),
            Sum(EntryKind.Payout),
            Sum(EntryKind.Tip),
            entries.Any(e => e.Kind == EntryKind.Payout)
                ? Sum(EntryKind.CaptureFee) + Sum(EntryKind.Commission) - Sum(EntryKind.Payout)
                : null,
            LedgerEntry.Currency,
            entries.Count,
            entries.Max(e => e.BookedAt)
        );
    }
}
