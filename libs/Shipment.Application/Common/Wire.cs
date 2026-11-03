using Shipment.Domain.Dispatch;
using Shipment.Persistence;

namespace Shipment.Application.Common;

/// <summary>The names of states as other contexts and the API see them (snake_case, as stored).</summary>
public static class Wire
{
    public static string Name(ShipmentStatus status) =>
        SnakeCaseEnumConverter<ShipmentStatus>.ToName(status);
}
