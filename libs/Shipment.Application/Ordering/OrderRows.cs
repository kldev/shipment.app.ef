using Shipment.Domain.Common;
using Shipment.Domain.Ordering;

namespace Shipment.Application.Ordering;

/// <summary>An order's sums over its lines - as picked once picking ended, as ordered before.</summary>
internal static class OrderRows
{
    public static OrderRow ToRow(this Order o, DateTimeOffset now)
    {
        var counted = o
            .Lines.Select(l => (Line: l, Quantity: l.PickedQuantity ?? l.Quantity))
            .ToList();
        var carried = counted.Where(c => c.Quantity > 0).ToList();
        return new OrderRow(
            o.Id,
            o.Code,
            o.Status,
            o.CustomerId,
            o.CustomerName,
            o.AddressId,
            o.AddressLabel,
            o.AddressLine,
            o.AddressCity,
            o.AddressPostcode,
            o.AddressNotes,
            o.AddressLatitude,
            o.AddressLongitude,
            o.PartnerId,
            o.PartnerName,
            o.PickupPointId,
            o.PickupPointName,
            o.PickupAddressLine,
            o.PickupLatitude,
            o.PickupLongitude,
            o.DistanceM,
            o.DeliveryFeeMinor,
            o.GoodsOrderedMinor,
            o.GoodsPickedMinor,
            (o.GoodsPickedMinor ?? o.GoodsOrderedMinor) + o.DeliveryFeeMinor + o.TipMinor,
            Order.Currency,
            o.Lines.Count,
            counted.Sum(c => c.Quantity),
            o.Lines.Count(l => l.PickedQuantity < l.Quantity),
            counted.Sum(c => c.Line.WeightG * c.Quantity),
            carried.Count == 0 ? null : carried.Max(c => c.Line.SizeClass),
            carried.Any(c => c.Line.AgeRestricted),
            o.CancelReason,
            o.FailureReason,
            o.TipPercent,
            o.TipMinor,
            o.PlacedAt,
            o.PickingStartedAt,
            o.ReadyAt,
            o.CancelledAt,
            o.DeliveredAt,
            o.FailedAt,
            (int)Math.Round((now - o.PlacedAt).TotalMinutes),
            o.Version
        );
    }

    public static OrderLineRow ToRow(this OrderLine l) =>
        new(
            l.OrderId,
            l.LineNo,
            l.ProductId,
            l.Sku,
            l.Name,
            l.UnitPriceMinor,
            l.Quantity,
            l.PickedQuantity,
            l.PickedQuantity is { } picked ? l.Quantity - picked : null,
            l.UnitPriceMinor * (l.PickedQuantity ?? l.Quantity),
            l.WeightG,
            l.SizeClass,
            l.AgeRestricted
        );
}
