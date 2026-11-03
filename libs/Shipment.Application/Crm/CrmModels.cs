using Shipment.Domain.Crm;

namespace Shipment.Application.Crm;

public sealed record PartnerRow(
    Guid Id,
    string Code,
    string Name,
    PartnerKind Kind,
    int CommissionBps,
    PartnerStatus Status,
    int ActivePickupPoints,
    DateTimeOffset CreatedAt,
    uint RowVersion
);

public sealed record PickupPointRow(
    Guid Id,
    Guid PartnerId,
    string PartnerName,
    PartnerKind PartnerKind,
    PartnerStatus PartnerStatus,
    string Name,
    string AddressLine,
    string City,
    string Postcode,
    decimal Latitude,
    decimal Longitude,
    int OpensMinute,
    int ClosesMinute,
    PickupPointStatus Status,
    bool IsOpenNow,
    DateTimeOffset CreatedAt,
    uint RowVersion
);

public sealed record CustomerRow(
    Guid Id,
    string DisplayName,
    string Email,
    string? Phone,
    int AddressCount,
    string? DefaultCity,
    DateTimeOffset CreatedAt,
    uint RowVersion
);

public sealed record CustomerAddressRow(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string Label,
    string AddressLine,
    string City,
    string Postcode,
    decimal Latitude,
    decimal Longitude,
    string? Notes,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    uint RowVersion
);

public sealed record CreatePartner(
    Guid Id,
    string Code,
    string Name,
    PartnerKind Kind,
    int CommissionBps
);

public sealed record UpdatePartner(
    Guid Id,
    string Code,
    string Name,
    PartnerKind Kind,
    int CommissionBps,
    PartnerStatus Status,
    uint RowVersion
);

public sealed record CreatePickupPoint(
    Guid Id,
    Guid PartnerId,
    string Name,
    string AddressLine,
    string City,
    string Postcode,
    decimal Latitude,
    decimal Longitude,
    int OpensMinute,
    int ClosesMinute
);

public sealed record UpdatePickupPoint(
    Guid Id,
    string Name,
    string AddressLine,
    string City,
    string Postcode,
    decimal Latitude,
    decimal Longitude,
    int OpensMinute,
    int ClosesMinute,
    PickupPointStatus Status,
    uint RowVersion
);

public sealed record CreateCustomer(Guid Id, string DisplayName, string Email, string? Phone);

public sealed record UpdateCustomer(
    Guid Id,
    string DisplayName,
    string Email,
    string? Phone,
    uint RowVersion
);

public sealed record CreateCustomerAddress(
    Guid Id,
    Guid CustomerId,
    string Label,
    string AddressLine,
    string City,
    string Postcode,
    decimal Latitude,
    decimal Longitude,
    string? Notes,
    bool IsDefault
);

public sealed record UpdateCustomerAddress(
    Guid Id,
    string Label,
    string AddressLine,
    string City,
    string Postcode,
    decimal Latitude,
    decimal Longitude,
    string? Notes,
    bool IsDefault,
    uint RowVersion
);
