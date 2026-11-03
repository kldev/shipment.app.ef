using Shipment.Domain.Common;
using Shipment.Domain.Ordering;

namespace Shipment.Application.Ordering;

public sealed record OrderRow(
    Guid Id,
    string Code,
    OrderStatus Status,
    Guid CustomerId,
    string CustomerName,
    Guid AddressId,
    string AddressLabel,
    string AddressLine,
    string AddressCity,
    string AddressPostcode,
    string? AddressNotes,
    decimal AddressLatitude,
    decimal AddressLongitude,
    Guid PartnerId,
    string PartnerName,
    Guid PickupPointId,
    string PickupPointName,
    string PickupAddressLine,
    decimal PickupLatitude,
    decimal PickupLongitude,
    int DistanceM,
    long DeliveryFeeMinor,
    long GoodsOrderedMinor,
    long? GoodsPickedMinor,
    long TotalMinor,
    string Currency,
    int LineCount,
    int ItemCount,
    int MissingLines,
    int WeightG,
    SizeClass? LargestSizeClass,
    bool AgeRestricted,
    string? CancelReason,
    string? FailureReason,
    int TipPercent,
    long TipMinor,
    DateTimeOffset PlacedAt,
    DateTimeOffset? PickingStartedAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? FailedAt,
    int WaitingMinutes,
    uint RowVersion
);

public sealed record OrderLineRow(
    Guid OrderId,
    int LineNo,
    Guid ProductId,
    string Sku,
    string Name,
    long UnitPriceMinor,
    int Quantity,
    int? PickedQuantity,
    int? MissingQuantity,
    long LineTotalMinor,
    int WeightG,
    SizeClass SizeClass,
    bool AgeRestricted
);

/// <summary>Distance and delivery fee from a pickup point to an address; beyond the last band nothing is delivered.</summary>
public sealed record QuoteRow(int DistanceM, long? DeliveryFeeMinor, bool Deliverable);

public sealed record DeliveryFeeBandRow(int MaxDistanceM, long FeeMinor, string Currency);

public sealed record PickingQueueRow(
    Guid Id,
    string Code,
    OrderStatus Status,
    Guid PartnerId,
    string PartnerName,
    Guid PickupPointId,
    string PickupPointName,
    string CustomerName,
    int LineCount,
    int ItemCount,
    bool AgeRestricted,
    DateTimeOffset PlacedAt,
    DateTimeOffset? PickingStartedAt,
    int WaitingMinutes,
    uint RowVersion
);

public sealed record PickingHistoryRow(
    Guid Id,
    string Code,
    OrderStatus Status,
    Guid PartnerId,
    string PartnerName,
    Guid PickupPointId,
    string PickupPointName,
    string CustomerName,
    Guid? PickedBy,
    string? PickedByName,
    DateTimeOffset? PickingStartedAt,
    DateTimeOffset? PickingEndedAt,
    int PickingMinutes,
    int LineCount,
    int ItemCount,
    int ItemsPicked,
    int ItemsMissing,
    int MissingLineCount,
    bool HasMissing,
    long GoodsOrderedMinor,
    long? GoodsPickedMinor,
    string Currency,
    uint RowVersion
);

public sealed record OrderFilter(
    OrderStatus? Status = null,
    Guid? CustomerId = null,
    Guid? PartnerId = null,
    Guid? PickupPointId = null
);

public sealed record PickingHistoryFilter(
    Guid? PickupPointId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool? HasMissing = null
);

public sealed record LineInput(Guid ProductId, int Quantity);

public sealed record PlaceOrder(
    Guid Id,
    Guid CustomerId,
    Guid AddressId,
    Guid PickupPointId,
    IReadOnlyList<LineInput> Lines,
    int TipPercent = 0
);

public sealed record PlacedOrder(string Code, uint RowVersion);

public sealed record CancelOrder(Guid Id, string Reason, uint RowVersion);

public sealed record StartPicking(Guid Id, uint RowVersion);

public sealed record FinishPicking(Guid Id, IReadOnlyList<LinePick> Lines, uint RowVersion);

public sealed record PickingFinished(uint RowVersion, OrderStatus Status);
