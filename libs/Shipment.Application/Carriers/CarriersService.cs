using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Shipment.Application.Common;
using Shipment.Domain.Carriers;
using Shipment.Domain.Common;
using Shipment.Persistence;

namespace Shipment.Application.Carriers;

public sealed record CourierRow(
    Guid Id,
    string DisplayName,
    string Phone,
    string Email,
    string VehicleType,
    string VehicleName,
    string? VehiclePlate,
    ShiftStatus ShiftStatus,
    DateTimeOffset? ShiftStartedAt,
    decimal? LastLatitude,
    decimal? LastLongitude,
    DateTimeOffset? LastPositionAt,
    int? PositionAgeMinutes,
    DateTimeOffset CreatedAt,
    uint RowVersion
);

public sealed record VehicleTypeRow(
    string Code,
    string Name,
    int MaxLoadG,
    SizeClass MaxSizeClass,
    int MaxDistanceM,
    int AvgSpeedKmh,
    bool NeedsLicence,
    int SortOrder
);

public sealed record CreateCourier(
    Guid Id,
    string DisplayName,
    string Phone,
    string Email,
    string VehicleType,
    string? VehiclePlate = null
);

public sealed record UpdateCourier(
    Guid Id,
    string DisplayName,
    string Phone,
    string Email,
    string VehicleType,
    uint RowVersion,
    string? VehiclePlate = null
);

public sealed record ChangeShift(Guid Id, uint RowVersion);

public sealed record SetCourierPosition(Guid Id, decimal Latitude, decimal Longitude);

/// <summary>The carriers context: couriers, their vehicle, shift and last known position.</summary>
// Created by DI (ApplicationSetup.AddShipmentApplication).
[UsedImplicitly]
public sealed partial class CarriersService(
    IDbContextFactory<ShipmentDbContext> factory,
    TimeProvider time,
    ICurrentActor actor
)
{
    private const string CourierName = "Courier";

    public async Task<IReadOnlyList<CourierRow>> ListCouriersAsync(
        string? vehicleType = null,
        ShiftStatus? shiftStatus = null,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var couriers = db.Couriers.AsQueryable();
        if (Text.Clean(vehicleType) is { } vehicle)
            couriers = couriers.Where(c => c.VehicleType == vehicle);
        if (shiftStatus is { } shift)
            couriers = couriers.Where(c => c.ShiftStatus == shift);
        return await RowsAsync(db, couriers.OrderBy(c => c.DisplayName).Take(Lists.Max), ct);
    }

    public async Task<CourierRow?> GetCourierAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await RowsAsync(db, db.Couriers.Where(c => c.Id == id), ct)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<VehicleTypeRow>> ListVehicleTypesAsync(
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db
            .VehicleTypes.OrderBy(v => v.SortOrder)
            .Select(v => new VehicleTypeRow(
                v.Code,
                v.Name,
                v.MaxLoadG,
                v.MaxSizeClass,
                v.MaxDistanceM,
                v.AvgSpeedKmh,
                v.NeedsLicence,
                v.SortOrder
            ))
            .ToListAsync(ct);
    }

    public Task<Result<uint>> CreateCourierAsync(
        CreateCourier command,
        CancellationToken ct = default
    ) => Races.OnceMoreOnConflictAsync(() => CreateCourierOnceAsync(command, ct));

    private async Task<Result<uint>> CreateCourierOnceAsync(
        CreateCourier command,
        CancellationToken ct
    )
    {
        if (command.Id == Guid.Empty)
            return Error.Validation("Courier id is required.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var input = await InputAsync(
            db,
            command.DisplayName,
            command.Phone,
            command.Email,
            command.VehicleType,
            command.VehiclePlate,
            ct
        );
        if (input.Error is { } invalid)
            return invalid;
        var data = input.Value;

        if (await db.Couriers.FindAsync([command.Id], ct) is { } existing)
            return existing.Same(data)
                ? existing.Version
                : Error.Conflict("This id is already used by another courier.");
        if (await db.Couriers.AnyAsync(c => c.Email == data.Email, ct))
            return Error.Conflict("Another courier already uses this e-mail.");

        var courier = new Courier(command.Id, data);
        db.Couriers.Add(courier);
        return await db.SaveAsync(CourierName, ct) is { Error: { } error }
            ? error
            : courier.Version;
    }

    public async Task<Result<uint>> UpdateCourierAsync(
        UpdateCourier command,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var input = await InputAsync(
            db,
            command.DisplayName,
            command.Phone,
            command.Email,
            command.VehicleType,
            command.VehiclePlate,
            ct
        );
        if (input.Error is { } invalid)
            return invalid;
        var data = input.Value;

        var courier = await db.Couriers.FindAsync([command.Id], ct);
        if (courier is null)
            return Error.NotFound("Courier not found.");
        if (!db.Expect(courier, command.RowVersion))
            return Error.Stale(CourierName);
        if (await db.Couriers.AnyAsync(c => c.Email == data.Email && c.Id != command.Id, ct))
            return Error.Conflict("Another courier already uses this e-mail.");
        var changed = courier.Update(data);
        if (changed.Error is { } refused)
            return refused;
        if (!changed.Value)
            return courier.Version;
        return await db.SaveAsync(CourierName, ct) is { Error: { } error }
            ? error
            : courier.Version;
    }

    /// <summary>On shift, acting for the courier (ADR 0006); already on shift is a no-op.</summary>
    public async Task<Result<uint>> StartShiftAsync(
        ChangeShift command,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var courier = await db.Couriers.FindAsync([command.Id], ct);
        if (courier is null)
            return Error.NotFound("Courier not found.");
        if (!db.Expect(courier, command.RowVersion))
            return Error.Stale(CourierName);
        if (!courier.StartShift(time.GetUtcNow()))
            return courier.Version;
        return await db.SaveAsync(CourierName, ct) is { Error: { } error }
            ? error
            : courier.Version;
    }

    /// <summary>Off shift; refused while the courier delivers an order, already off is a no-op.</summary>
    public async Task<Result<uint>> EndShiftAsync(
        ChangeShift command,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var courier = await db.Couriers.FindAsync([command.Id], ct);
        if (courier is null)
            return Error.NotFound("Courier not found.");
        if (!db.Expect(courier, command.RowVersion))
            return Error.Stale(CourierName);
        var changed = courier.EndShift();
        if (changed.Error is { } refused)
            return refused;
        if (!changed.Value)
            return courier.Version;
        return await db.SaveAsync(CourierName, ct) is { Error: { } error }
            ? error
            : courier.Version;
    }

    /// <summary>The admin moves the courier on the map, for the courier's phone. No row version: the latest wins.</summary>
    public async Task<Result<uint>> SetPositionAsync(
        SetCourierPosition command,
        CancellationToken ct = default
    )
    {
        var (latitude, longitude) = (
            Text.Coordinate(command.Latitude),
            Text.Coordinate(command.Longitude)
        );
        if (!Text.IsPlace(latitude, longitude))
            return Error.Validation(
                "Pick the place on the map: latitude -90..90, longitude -180..180."
            );
        // The latest position simply wins: one UPDATE, no row version (a shift change meanwhile is no conflict).
        var (now, by) = (time.GetUtcNow(), actor.UserId);
        await using var db = await factory.CreateDbContextAsync(ct);
        var moved = await db
            .Couriers.Where(c => c.Id == command.Id)
            .ExecuteUpdateAsync(
                set =>
                    set.SetProperty(c => c.LastLatitude, latitude)
                        .SetProperty(c => c.LastLongitude, longitude)
                        .SetProperty(c => c.LastPositionAt, now)
                        .SetProperty(c => c.ModifiedAt, now)
                        .SetProperty(c => c.ModifiedBy, by),
                ct
            );
        if (moved == 0)
            return Error.NotFound("Courier not found.");
        return await db
            .Couriers.Where(c => c.Id == command.Id)
            .Select(c => c.Version)
            .FirstAsync(ct);
    }

    /// <summary>
    /// The simulator's position of a courier (the runner, no actor) - only while on shift; the shift may have ended while
    /// it was moving. Not an edit of the courier: no audit columns change.
    /// </summary>
    public async Task ReportPositionAsync(
        Guid courierId,
        decimal latitude,
        decimal longitude,
        CancellationToken ct = default
    )
    {
        var (lat, lon, now) = (
            Text.Coordinate(latitude),
            Text.Coordinate(longitude),
            time.GetUtcNow()
        );
        await using var db = await factory.CreateDbContextAsync(ct);
        await db
            .Couriers.Where(c => c.Id == courierId && c.ShiftStatus != ShiftStatus.Offline)
            .ExecuteUpdateAsync(
                set =>
                    set.SetProperty(c => c.LastLatitude, lat)
                        .SetProperty(c => c.LastLongitude, lon)
                        .SetProperty(c => c.LastPositionAt, now),
                ct
            );
    }

    private static async Task<Result<CourierData>> InputAsync(
        ShipmentDbContext db,
        string? displayName,
        string? phone,
        string? email,
        string? vehicleType,
        string? vehiclePlate,
        CancellationToken ct
    )
    {
        var name = Text.Clean(displayName);
        if (name is null)
            return Error.Validation("Courier name is required.");
        if (vehicleType is null || !await db.VehicleTypes.AnyAsync(v => v.Code == vehicleType, ct))
            return Error.Validation("Vehicle must be a bike, a car or a van.");
        var plate = Text.Clean(Whitespace().Replace(vehiclePlate ?? "", ""))?.ToUpperInvariant();
        if (vehicleType != VehicleType.Bike && plate is null)
            return Error.Validation("A car or a van needs its registration plate.");
        var cleanEmail = Inputs.Email(email);
        if (cleanEmail.Error is { } emailError)
            return emailError;
        var cleanPhone = Text.Clean(phone);
        if (!Text.IsPhone(cleanPhone))
            return Error.Validation(
                "Phone may contain digits, spaces, brackets, dashes and a leading +."
            );
        return new CourierData(name, cleanPhone!, cleanEmail.Value, vehicleType, plate);
    }

    [GeneratedRegex(@"\s")]
    private static partial Regex Whitespace();

    private async Task<List<CourierRow>> RowsAsync(
        ShipmentDbContext db,
        IQueryable<Courier> couriers,
        CancellationToken ct
    )
    {
        var now = time.GetUtcNow();
        var rows = await couriers
            // A scalar subquery, not a join: the couriers keep the order the caller gave them.
            .Select(c => new
            {
                c,
                VehicleName = db
                    .VehicleTypes.Where(v => v.Code == c.VehicleType)
                    .Select(v => v.Name)
                    .First(),
            })
            .Select(x => new CourierRow(
                x.c.Id,
                x.c.DisplayName,
                x.c.Phone,
                x.c.Email,
                x.c.VehicleType,
                x.VehicleName,
                x.c.VehiclePlate,
                x.c.ShiftStatus,
                x.c.ShiftStartedAt,
                x.c.LastLatitude,
                x.c.LastLongitude,
                x.c.LastPositionAt,
                null,
                x.c.CreatedAt,
                x.c.Version
            ))
            .ToListAsync(ct);
        return rows.Select(r => r with { PositionAgeMinutes = Minutes(now, r.LastPositionAt) })
            .ToList();
    }

    /// <summary>Whole minutes since an instant; null when there is none.</summary>
    internal static int? Minutes(DateTimeOffset now, DateTimeOffset? since) =>
        since is { } at ? (int)Math.Round((now - at).TotalMinutes) : null;
}
