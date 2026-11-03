using Shipment.Domain.Billing;

namespace Shipment.Application.Billing;

public sealed record EntryRow(
    long Id,
    EntryKind Kind,
    Guid OrderId,
    string OrderCode,
    Guid PartnerId,
    Guid? ShipmentId,
    Guid? CourierId,
    int? LineNo,
    Guid? ProductId,
    int? Quantity,
    long? Basis,
    long AmountMinor,
    string Currency,
    string Description,
    DateTimeOffset BookedAt
);

/// <summary>
/// The money of one order from the ledger: authorized, captured, refunded, what the customer paid in the end, the
/// partner's commission and what the partner gets, the courier's payout and the operator's margin (fee + commission -
/// payout; known once delivered); the tip, captured and passed to the courier.
/// </summary>
public sealed record OrderBillingRow(
    Guid OrderId,
    string OrderCode,
    Guid PartnerId,
    long AuthorizedMinor,
    long CapturedGoodsMinor,
    long CapturedFeeMinor,
    long CapturedTipMinor,
    long RefundedMinor,
    long PaidMinor,
    long CommissionMinor,
    long PartnerPayableMinor,
    long PayoutMinor,
    long TipMinor,
    long? MarginMinor,
    string Currency,
    int EntryCount,
    DateTimeOffset LastBookedAt
);

/// <summary>A day of the operator: orders paid for, charged, refunded, commission, payouts, margin and tips passed on.</summary>
public sealed record DailySummaryRow(
    DateOnly Day,
    int Orders,
    long ChargedMinor,
    long RefundedMinor,
    long CommissionMinor,
    long PayoutMinor,
    long MarginMinor,
    long TipMinor,
    string Currency
);

public sealed record PartnerBalanceRow(
    Guid PartnerId,
    string? PartnerName,
    int? CommissionBps,
    int Deliveries,
    long GoodsMinor,
    long CommissionMinor,
    long PayableMinor,
    string Currency
);

public sealed record CourierPayoutRow(
    Guid CourierId,
    string? DisplayName,
    string? VehicleType,
    int Deliveries,
    long DistanceM,
    long PayoutMinor,
    long? BaseMinor,
    long? PerKmMinor,
    long TipMinor,
    string Currency
);

public sealed record PayoutRateRow(
    string VehicleType,
    long BaseMinor,
    long PerKmMinor,
    string Currency
);

public sealed record EntryFilter(
    Guid? OrderId = null,
    Guid? ShipmentId = null,
    Guid? PartnerId = null,
    Guid? CourierId = null,
    EntryKind? Kind = null
);
