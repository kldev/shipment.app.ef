using Shipment.Domain.Common;
using Shipment.Domain.Dispatch;

namespace Shipment.Application.Dispatch;

public sealed record ShipmentRow(
    Guid Id,
    string Code,
    ShipmentStatus Status,
    Guid OrderId,
    string OrderCode,
    Guid CustomerId,
    string CustomerName,
    string PartnerName,
    Guid PickupPointId,
    string PickupPointName,
    string PickupAddressLine,
    decimal PickupLatitude,
    decimal PickupLongitude,
    string DropoffLabel,
    string DropoffAddressLine,
    string DropoffCity,
    string? DropoffNotes,
    decimal DropoffLatitude,
    decimal DropoffLongitude,
    int DistanceM,
    int ItemCount,
    int WeightG,
    SizeClass LargestSizeClass,
    string RequiredVehicle,
    string RequiredVehicleName,
    bool AgeCheckRequired,
    bool AgeChecked,
    Guid? CourierId,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? ArrivedAtPickupAt,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? ArrivedAtCustomerAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? FailedAt,
    int WaitingMinutes,
    uint RowVersion
);

/// <summary>A courier who can take a shipment now, with the straight-line distance to its pickup point.</summary>
public sealed record ProposalRow(
    Guid CourierId,
    string DisplayName,
    string Phone,
    string VehicleType,
    string VehicleName,
    string? VehiclePlate,
    decimal? LastLatitude,
    decimal? LastLongitude,
    int? PositionAgeMinutes,
    int? DistanceM
);

public sealed record ShipmentFilter(
    ShipmentStatus? Status = null,
    Guid? OrderId = null,
    Guid? CourierId = null
);

public sealed record AssignCourier(Guid Id, Guid CourierId, uint RowVersion);

public sealed record AdvanceShipment(
    Guid Id,
    ShipmentStatus Status,
    uint RowVersion,
    bool AgeChecked = false
);

public sealed record FailShipment(Guid Id, string Reason, uint RowVersion);

public sealed record UnassignCourier(Guid Id, uint RowVersion);
