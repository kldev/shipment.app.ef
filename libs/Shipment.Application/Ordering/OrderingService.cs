using Microsoft.EntityFrameworkCore;
using Shipment.Application.Common;
using Shipment.Domain.Common;
using Shipment.Domain.Crm;
using Shipment.Domain.Ordering;
using Shipment.Messaging.Contracts;
using Shipment.Persistence;

namespace Shipment.Application.Ordering;

/// <summary>
/// The ordering context: a customer's order from one partner's open pickup point to one of the customer's addresses.
/// The order copies what it needs when placed; its events go to the outbox in the same save (ADR 0007).
/// </summary>
public sealed class OrderingService(IDbContextFactory<ShipmentDbContext> factory, TimeProvider time)
{
    private const string OrderName = "Order";

    public async Task<IReadOnlyList<OrderRow>> ListOrdersAsync(
        OrderFilter? filter = null,
        int take = Lists.Max,
        CancellationToken ct = default
    )
    {
        filter ??= new OrderFilter();
        await using var db = await factory.CreateDbContextAsync(ct);
        var orders = db.Orders.AsNoTracking().Include(o => o.Lines).AsQueryable();
        if (filter.Status is { } status)
            orders = orders.Where(o => o.Status == status);
        if (filter.CustomerId is { } customer)
            orders = orders.Where(o => o.CustomerId == customer);
        if (filter.PartnerId is { } partner)
            orders = orders.Where(o => o.PartnerId == partner);
        if (filter.PickupPointId is { } point)
            orders = orders.Where(o => o.PickupPointId == point);
        var list = await orders
            .OrderByDescending(o => o.PlacedAt)
            .ThenBy(o => o.Id)
            .Take(Math.Clamp(take, 1, Lists.Max))
            .AsSplitQuery()
            .ToListAsync(ct);
        var now = time.GetUtcNow();
        return list.Select(o => o.ToRow(now)).ToList();
    }

    public async Task<OrderRow?> GetOrderAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db
            .Orders.AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        return order?.ToRow(time.GetUtcNow());
    }

    public async Task<IReadOnlyList<OrderLineRow>> ListLinesAsync(
        Guid orderId,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var lines = await db
            .OrderLines.AsNoTracking()
            .Where(l => l.OrderId == orderId)
            .OrderBy(l => l.LineNo)
            .ToListAsync(ct);
        return lines.Select(l => l.ToRow()).ToList();
    }

    public async Task<IReadOnlyList<DeliveryFeeBandRow>> ListFeeBandsAsync(
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db
            .DeliveryFeeBands.OrderBy(b => b.MaxDistanceM)
            .Select(b => new DeliveryFeeBandRow(b.MaxDistanceM, b.FeeMinor, Order.Currency))
            .ToListAsync(ct);
    }

    /// <summary>The summary before placing; null when the point or the address is unknown.</summary>
    public async Task<QuoteRow?> QuoteAsync(
        Guid pickupPointId,
        Guid addressId,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var point = await db
            .PickupPoints.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pickupPointId, ct);
        var address = await db
            .CustomerAddresses.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == addressId, ct);
        if (point is null || address is null)
            return null;
        var distance = Distance.Metres(
            point.Latitude,
            point.Longitude,
            address.Latitude,
            address.Longitude
        );
        var fee = DeliveryFeeBand.FeeFor(await db.DeliveryFeeBands.ToListAsync(ct), distance);
        return new QuoteRow(distance, fee, fee is not null);
    }

    /// <summary>
    /// Places an order for a customer (the admin acts for the customer, ADR 0006). The id comes from the page (ADR 0009):
    /// the same order again answers as the first time, a different one under the same id is a conflict.
    /// </summary>
    public Task<Result<PlacedOrder>> PlaceAsync(
        PlaceOrder command,
        CancellationToken ct = default
    ) => Races.OnceMoreOnConflictAsync(() => PlaceOnceAsync(command, ct));

    private async Task<Result<PlacedOrder>> PlaceOnceAsync(PlaceOrder command, CancellationToken ct)
    {
        if (command.Id == Guid.Empty)
            return Error.Validation("Order id is required.");
        if (command.Lines is not { Count: > 0 })
            return Error.Validation("Add at least one product.");
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - a JSON body may carry null
        if (
            command.Lines.Any(l =>
                l is null
                || l.ProductId == Guid.Empty
                || l.Quantity is < 1 or > OrderLine.MaxQuantity
            )
        )
            return Error.Validation("Each line needs a product and a quantity from 1 to 99.");
        if (command.Lines.Select(l => l.ProductId).Distinct().Count() != command.Lines.Count)
            return Error.Validation("Each product only once - change its quantity instead.");
        if (!Order.TipPercents.Contains(command.TipPercent))
            return Error.Validation("The tip is 0, 5 or 10 percent.");

        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db
            .Orders.AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == command.Id, ct);
        if (existing is not null)
            return existing.Same(
                command.CustomerId,
                command.AddressId,
                command.PickupPointId,
                command.Lines.Select(l => (l.ProductId, l.Quantity)).ToList()
            )
                ? new PlacedOrder(existing.Code, existing.Version)
                : Error.Conflict("This id is already used by another order.");

        var customer = await db
            .Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == command.CustomerId, ct);
        if (customer is null)
            return Error.NotFound("Customer not found.");
        var address = await db
            .CustomerAddresses.AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.Id == command.AddressId && a.CustomerId == command.CustomerId,
                ct
            );
        if (address is null)
            return Error.NotFound("This address is not one of the customer's.");
        var point = await db
            .PickupPoints.AsNoTracking()
            .Include(p => p.Partner)
            .FirstOrDefaultAsync(p => p.Id == command.PickupPointId, ct);
        if (point is null)
            return Error.NotFound("Pickup point not found.");
        if (
            point.Status != PickupPointStatus.Active
            || point.Partner.Status != PartnerStatus.Active
        )
            return Error.Validation("This pickup point takes no orders now.");

        var ids = command.Lines.Select(l => l.ProductId).ToList();
        var products = await db
            .Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.PartnerId == point.PartnerId)
            .ToDictionaryAsync(p => p.Id, ct);
        var missing = command
            .Lines.Select(l => products.GetValueOrDefault(l.ProductId))
            .Where(p => p is not { ListedAvailable: true })
            .Select(p => p?.Name ?? "an unknown product")
            .Order(StringComparer.Ordinal)
            .ToList();
        if (missing.Count > 0)
            return Error.Validation(
                $"Not available from this partner: {string.Join(", ", missing)}."
            );

        var distance = Distance.Metres(
            point.Latitude,
            point.Longitude,
            address.Latitude,
            address.Longitude
        );
        var fee = DeliveryFeeBand.FeeFor(await db.DeliveryFeeBands.ToListAsync(ct), distance);
        if (fee is null)
            return Error.Validation(
                $"The address is too far from the pickup point ({distance / 1000m:0.#} km)."
            );

        var now = time.GetUtcNow();
        var order = new Order(
            command.Id,
            await db.NextOrderCodeAsync(ct),
            new OrderDropoff(
                customer.Id,
                customer.DisplayName,
                address.Id,
                address.Label,
                address.AddressLine,
                address.City,
                address.Postcode,
                address.Notes,
                address.Latitude,
                address.Longitude
            ),
            new OrderPickup(
                point.PartnerId,
                point.Partner.Name,
                point.Id,
                point.Name,
                point.AddressLine,
                point.Latitude,
                point.Longitude
            ),
            command
                .Lines.Select(l =>
                {
                    var p = products[l.ProductId];
                    return (
                        new OrderProduct(
                            p.Id,
                            p.Sku,
                            p.Name,
                            p.PriceMinor,
                            p.WeightG,
                            p.SizeClass,
                            p.AgeRestricted
                        ),
                        l.Quantity
                    );
                })
                .ToList(),
            distance,
            fee.Value,
            command.TipPercent,
            now
        );
        db.Orders.Add(order);
        db.Enqueue(new OrderPlaced(order.Id, order.Code), now);
        return await db.SaveAsync(OrderName, ct) is { Error: { } error }
            ? error
            : new PlacedOrder(order.Code, order.Version);
    }

    /// <summary>Before the store starts packing, with a reason the customer reads; billing refunds in full.</summary>
    public async Task<Result<uint>> CancelAsync(CancelOrder command, CancellationToken ct = default)
    {
        var reason = Text.Clean(command.Reason);
        if (reason is null)
            return Error.Validation("Say why the order is cancelled - the customer reads it.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.Orders.FindAsync([command.Id], ct);
        if (order is null)
            return Error.NotFound("Order not found.");
        if (!db.Expect(order, command.RowVersion))
            return Error.Stale(OrderName);
        var now = time.GetUtcNow();
        if (order.Cancel(reason, now).Error is { } refused)
            return refused;
        db.Enqueue(new OrderCancelled(order.Id, reason), now);
        return await db.SaveAsync(OrderName, ct) is { Error: { } error } ? error : order.Version;
    }

    /// <summary>
    /// The runner's reaction to a shipment that ended (ADR 0007): delivered or failed with the courier's reason. A
    /// repeated event is a no-op; an order that is not ready is a refusal the runner retries, then dead-letters.
    /// </summary>
    public async Task<Result> CompleteAsync(
        Guid orderId,
        bool delivered,
        string? reason,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.Orders.FindAsync([orderId], ct);
        if (order is null)
            return Error.NotFound("Order not found.");
        var changed = order.Complete(delivered, reason, time.GetUtcNow());
        if (changed.Error is { } refused)
            return refused;
        return changed.Value ? await db.SaveAsync(OrderName, ct) : Result.Success;
    }
}
