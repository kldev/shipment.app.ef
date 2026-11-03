using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shipment.Application.Common;
using Shipment.Domain.Common;
using Shipment.Domain.Crm;
using Shipment.Persistence;

namespace Shipment.Application.Crm;

/// <summary>
/// The crm context: partners and their pickup points, customers and their delivery addresses. Ids come from the
/// client (ADR 0009): the same id with the same values again is a retried request and answers as the first one did,
/// with other values it is a conflict.
/// </summary>
public sealed partial class CrmService(
    IDbContextFactory<ShipmentDbContext> factory,
    TimeProvider time
)
{
    private const string PartnerName = "Partner";
    private const string PickupPointName = "Pickup point";
    private const string CustomerName = "Customer";
    private const string AddressName = "Address";

    // ---- Partners ----

    public async Task<IReadOnlyList<PartnerRow>> ListPartnersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await PartnerRows(db, db.Partners.OrderBy(p => p.Name).Take(Lists.Max))
            .ToListAsync(ct);
    }

    public async Task<PartnerRow?> GetPartnerAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await PartnerRows(db, db.Partners.Where(p => p.Id == id)).FirstOrDefaultAsync(ct);
    }

    public Task<Result<uint>> CreatePartnerAsync(
        CreatePartner command,
        CancellationToken ct = default
    ) => Races.OnceMoreOnConflictAsync(() => CreatePartnerOnceAsync(command, ct));

    private async Task<Result<uint>> CreatePartnerOnceAsync(
        CreatePartner command,
        CancellationToken ct
    )
    {
        if (Inputs.Required(command.Id, PartnerName) is { } missing)
            return missing;
        var input = PartnerInput(command.Code, command.Name, command.CommissionBps);
        if (input.Error is { } invalid)
            return invalid;
        var (code, name) = input.Value;

        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Partners.FindAsync([command.Id], ct) is { } existing)
            return existing.Same(code, name, command.Kind, command.CommissionBps)
                ? existing.Version
                : Error.Conflict("This id is already used by another partner.");
        if (await db.Partners.AnyAsync(p => p.Code == code, ct))
            return Error.Conflict("Another partner already uses this code.");

        var partner = new Partner(command.Id, code, name, command.Kind, command.CommissionBps);
        db.Partners.Add(partner);
        return await db.SaveAsync(PartnerName, ct) is { Error: { } error }
            ? error
            : partner.Version;
    }

    public async Task<Result<uint>> UpdatePartnerAsync(
        UpdatePartner command,
        CancellationToken ct = default
    )
    {
        var input = PartnerInput(command.Code, command.Name, command.CommissionBps);
        if (input.Error is { } invalid)
            return invalid;
        var (code, name) = input.Value;

        await using var db = await factory.CreateDbContextAsync(ct);
        var partner = await db.Partners.FindAsync([command.Id], ct);
        if (partner is null)
            return Error.NotFound("Partner not found.");
        if (!db.Expect(partner, command.RowVersion))
            return Error.Stale(PartnerName);
        if (await db.Partners.AnyAsync(p => p.Code == code && p.Id != command.Id, ct))
            return Error.Conflict("Another partner already uses this code.");
        if (!partner.Update(code, name, command.Kind, command.CommissionBps, command.Status))
            return partner.Version;
        return await db.SaveAsync(PartnerName, ct) is { Error: { } error }
            ? error
            : partner.Version;
    }

    // ---- Pickup points ----

    public async Task<IReadOnlyList<PickupPointRow>> ListPickupPointsAsync(
        Guid? partnerId = null,
        PickupPointStatus? status = null,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var points = db.PickupPoints.AsQueryable();
        if (partnerId is { } partner)
            points = points.Where(p => p.PartnerId == partner);
        if (status is { } wanted)
            points = points.Where(p => p.Status == wanted);
        return await points
            .OrderBy(p => p.Partner.Name)
            .ThenBy(p => p.Name)
            .Take(Lists.Max)
            .Select(PickupPointRow(OperatorTime.MinuteOfDay(time.GetUtcNow())))
            .ToListAsync(ct);
    }

    public async Task<PickupPointRow?> GetPickupPointAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db
            .PickupPoints.Where(p => p.Id == id)
            .Select(PickupPointRow(OperatorTime.MinuteOfDay(time.GetUtcNow())))
            .FirstOrDefaultAsync(ct);
    }

    public Task<Result<uint>> CreatePickupPointAsync(
        CreatePickupPoint command,
        CancellationToken ct = default
    ) => Races.OnceMoreOnConflictAsync(() => CreatePickupPointOnceAsync(command, ct));

    private async Task<Result<uint>> CreatePickupPointOnceAsync(
        CreatePickupPoint command,
        CancellationToken ct
    )
    {
        if (Inputs.Required(command.Id, PickupPointName) is { } missing)
            return missing;
        var input = PickupPointInput(
            command.Name,
            command.AddressLine,
            command.City,
            command.Postcode,
            command.Latitude,
            command.Longitude,
            command.OpensMinute,
            command.ClosesMinute
        );
        if (input.Error is { } invalid)
            return invalid;
        var (name, place) = input.Value;

        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.PickupPoints.FindAsync([command.Id], ct) is { } existing)
            return existing.Same(
                command.PartnerId,
                name,
                place,
                command.OpensMinute,
                command.ClosesMinute
            )
                ? existing.Version
                : Error.Conflict("This id is already used by another pickup point.");
        if (!await db.Partners.AnyAsync(p => p.Id == command.PartnerId, ct))
            return Error.NotFound("Partner not found.");

        var point = new PickupPoint(
            command.Id,
            command.PartnerId,
            name,
            place,
            command.OpensMinute,
            command.ClosesMinute
        );
        db.PickupPoints.Add(point);
        return await db.SaveAsync(PickupPointName, ct) is { Error: { } error }
            ? error
            : point.Version;
    }

    public async Task<Result<uint>> UpdatePickupPointAsync(
        UpdatePickupPoint command,
        CancellationToken ct = default
    )
    {
        var input = PickupPointInput(
            command.Name,
            command.AddressLine,
            command.City,
            command.Postcode,
            command.Latitude,
            command.Longitude,
            command.OpensMinute,
            command.ClosesMinute
        );
        if (input.Error is { } invalid)
            return invalid;
        var (name, place) = input.Value;

        await using var db = await factory.CreateDbContextAsync(ct);
        var point = await db.PickupPoints.FindAsync([command.Id], ct);
        if (point is null)
            return Error.NotFound("Pickup point not found.");
        if (!db.Expect(point, command.RowVersion))
            return Error.Stale(PickupPointName);
        if (!point.Update(name, place, command.OpensMinute, command.ClosesMinute, command.Status))
            return point.Version;
        return await db.SaveAsync(PickupPointName, ct) is { Error: { } error }
            ? error
            : point.Version;
    }

    // ---- Customers ----

    public async Task<IReadOnlyList<CustomerRow>> ListCustomersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await CustomerRows(db.Customers.OrderBy(c => c.DisplayName).Take(Lists.Max))
            .ToListAsync(ct);
    }

    public async Task<CustomerRow?> GetCustomerAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await CustomerRows(db.Customers.Where(c => c.Id == id)).FirstOrDefaultAsync(ct);
    }

    public Task<Result<uint>> CreateCustomerAsync(
        CreateCustomer command,
        CancellationToken ct = default
    ) => Races.OnceMoreOnConflictAsync(() => CreateCustomerOnceAsync(command, ct));

    private async Task<Result<uint>> CreateCustomerOnceAsync(
        CreateCustomer command,
        CancellationToken ct
    )
    {
        if (Inputs.Required(command.Id, CustomerName) is { } missing)
            return missing;
        var input = CustomerInput(command.DisplayName, command.Email, command.Phone);
        if (input.Error is { } invalid)
            return invalid;
        var (name, email, phone) = input.Value;

        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Customers.FindAsync([command.Id], ct) is { } existing)
            return existing.Same(name, email, phone)
                ? existing.Version
                : Error.Conflict("This id is already used by another customer.");
        if (await db.Customers.AnyAsync(c => c.Email == email, ct))
            return Error.Conflict("Another customer already uses this e-mail.");

        var customer = new Customer(command.Id, name, email, phone);
        db.Customers.Add(customer);
        return await db.SaveAsync(CustomerName, ct) is { Error: { } error }
            ? error
            : customer.Version;
    }

    public async Task<Result<uint>> UpdateCustomerAsync(
        UpdateCustomer command,
        CancellationToken ct = default
    )
    {
        var input = CustomerInput(command.DisplayName, command.Email, command.Phone);
        if (input.Error is { } invalid)
            return invalid;
        var (name, email, phone) = input.Value;

        await using var db = await factory.CreateDbContextAsync(ct);
        var customer = await db.Customers.FindAsync([command.Id], ct);
        if (customer is null)
            return Error.NotFound("Customer not found.");
        if (!db.Expect(customer, command.RowVersion))
            return Error.Stale(CustomerName);
        if (await db.Customers.AnyAsync(c => c.Email == email && c.Id != command.Id, ct))
            return Error.Conflict("Another customer already uses this e-mail.");
        if (!customer.Update(name, email, phone))
            return customer.Version;
        return await db.SaveAsync(CustomerName, ct) is { Error: { } error }
            ? error
            : customer.Version;
    }

    // ---- Delivery addresses ----

    public async Task<IReadOnlyList<CustomerAddressRow>> ListAddressesAsync(
        Guid? customerId = null,
        bool? isDefault = null,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var addresses = db.CustomerAddresses.AsQueryable();
        if (customerId is { } customer)
            addresses = addresses.Where(a => a.CustomerId == customer);
        if (isDefault is { } flag)
            addresses = addresses.Where(a => a.IsDefault == flag);
        return await AddressRows(
                db,
                addresses.OrderByDescending(a => a.IsDefault).ThenBy(a => a.Label).Take(Lists.Max)
            )
            .ToListAsync(ct);
    }

    public Task<Result<uint>> CreateAddressAsync(
        CreateCustomerAddress command,
        CancellationToken ct = default
    ) => Races.OnceMoreOnConflictAsync(() => CreateAddressOnceAsync(command, ct));

    private async Task<Result<uint>> CreateAddressOnceAsync(
        CreateCustomerAddress command,
        CancellationToken ct
    )
    {
        if (Inputs.Required(command.Id, AddressName) is { } missing)
            return missing;
        var input = AddressInput(
            command.Label,
            command.AddressLine,
            command.City,
            command.Postcode,
            command.Latitude,
            command.Longitude,
            command.Notes
        );
        if (input.Error is { } invalid)
            return invalid;
        var (label, place, notes) = input.Value;

        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.CustomerAddresses.FindAsync([command.Id], ct) is { } existing)
            return existing.CustomerId == command.CustomerId && existing.Same(label, place, notes)
                ? existing.Version
                : Error.Conflict("This id is already used by another address.");
        var customer = await db
            .Customers.Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == command.CustomerId, ct);
        if (customer is null)
            return Error.NotFound("Customer not found.");

        var address = customer.AddAddress(command.Id, label, place, notes, command.IsDefault);
        TouchCustomer(db, customer);
        return await db.SaveAsync(AddressName, ct) is { Error: { } error }
            ? error
            : address.Version;
    }

    public async Task<Result<uint>> UpdateAddressAsync(
        UpdateCustomerAddress command,
        CancellationToken ct = default
    )
    {
        var input = AddressInput(
            command.Label,
            command.AddressLine,
            command.City,
            command.Postcode,
            command.Latitude,
            command.Longitude,
            command.Notes
        );
        if (input.Error is { } invalid)
            return invalid;
        var (label, place, notes) = input.Value;

        await using var db = await factory.CreateDbContextAsync(ct);
        var customerId = await db
            .CustomerAddresses.Where(a => a.Id == command.Id)
            .Select(a => (Guid?)a.CustomerId)
            .FirstOrDefaultAsync(ct);
        if (customerId is null)
            return Error.NotFound("Address not found.");
        var customer = await db
            .Customers.Include(c => c.Addresses)
            .FirstAsync(c => c.Id == customerId, ct);
        var address = customer.Addresses.First(a => a.Id == command.Id);
        if (!db.Expect(address, command.RowVersion))
            return Error.Stale(AddressName);
        if (!customer.UpdateAddress(address, label, place, notes, command.IsDefault))
            return address.Version;
        TouchCustomer(db, customer);
        return await db.SaveAsync(AddressName, ct) is { Error: { } error }
            ? error
            : address.Version;
    }

    /// <summary>
    /// An address change is a change of the customer aggregate: its row is updated too, so two changes of the default
    /// at once cannot both win (the second one's customer version is stale).
    /// </summary>
    private static void TouchCustomer(ShipmentDbContext db, Customer customer) =>
        db.Entry(customer).Property(c => c.ModifiedAt).IsModified = true;

    // ---- Inputs ----

    private static Result<(string Code, string Name)> PartnerInput(
        string? code,
        string? name,
        int commissionBps
    )
    {
        var cleanName = Text.Clean(name);
        if (cleanName is null)
            return Error.Validation("Partner name is required.");
        var cleanCode = Text.Clean(code)?.ToUpperInvariant();
        if (cleanCode is null || !PartnerCode().IsMatch(cleanCode))
            return Error.Validation("Partner code must be 2 to 12 letters or digits.");
        if (commissionBps is < 0 or > Partner.MaxCommissionBps)
            return Error.Validation("Commission must be between 0 and 50 %.");
        return (cleanCode, cleanName);
    }

    private static Result<(string Name, Place Place)> PickupPointInput(
        string? name,
        string? addressLine,
        string? city,
        string? postcode,
        decimal latitude,
        decimal longitude,
        int opensMinute,
        int closesMinute
    )
    {
        var cleanName = Text.Clean(name);
        if (cleanName is null)
            return Error.Validation("Pickup point name is required.");
        var place = Inputs.Place(addressLine, city, postcode, latitude, longitude);
        if (place.Error is { } error)
            return error;
        if (!PickupPoint.ValidHours(opensMinute, closesMinute))
            return Error.Validation(
                "Opening hours: it must open before it closes, within one day."
            );
        return (cleanName, place.Value);
    }

    private static Result<(string Name, string Email, string? Phone)> CustomerInput(
        string? displayName,
        string? email,
        string? phone
    )
    {
        var name = Text.Clean(displayName);
        if (name is null)
            return Error.Validation("Customer name is required.");
        var cleanEmail = Inputs.Email(email);
        if (cleanEmail.Error is { } emailError)
            return emailError;
        var cleanPhone = Inputs.OptionalPhone(phone);
        if (cleanPhone.Error is { } phoneError)
            return phoneError;
        return (name, cleanEmail.Value, cleanPhone.Value);
    }

    private static Result<(string Label, Place Place, string? Notes)> AddressInput(
        string? label,
        string? addressLine,
        string? city,
        string? postcode,
        decimal latitude,
        decimal longitude,
        string? notes
    )
    {
        var cleanLabel = Text.Clean(label);
        if (cleanLabel is null)
            return Error.Validation("Give the address a label, e.g. Home.");
        var place = Inputs.Place(addressLine, city, postcode, latitude, longitude);
        if (place.Error is { } error)
            return error;
        return (cleanLabel, place.Value, Text.Clean(notes));
    }

    [GeneratedRegex("^[A-Z0-9]{2,12}$")]
    private static partial Regex PartnerCode();

    // ---- Projections ----

    // Filter and order the entities first: a positional record in the projection cannot be ordered by in SQL.
    private static IQueryable<PartnerRow> PartnerRows(
        ShipmentDbContext db,
        IQueryable<Partner> partners
    ) =>
        partners.Select(p => new PartnerRow(
            p.Id,
            p.Code,
            p.Name,
            p.Kind,
            p.CommissionBps,
            p.Status,
            db.PickupPoints.Count(pp =>
                pp.PartnerId == p.Id && pp.Status == PickupPointStatus.Active
            ),
            p.CreatedAt,
            p.Version
        ));

    /// <summary>Open now in the operator's zone: the point and its partner active, the minute within the hours.</summary>
    private static Expression<Func<PickupPoint, PickupPointRow>> PickupPointRow(int minute) =>
        pp => new PickupPointRow(
            pp.Id,
            pp.PartnerId,
            pp.Partner.Name,
            pp.Partner.Kind,
            pp.Partner.Status,
            pp.Name,
            pp.AddressLine,
            pp.City,
            pp.Postcode,
            pp.Latitude,
            pp.Longitude,
            pp.OpensMinute,
            pp.ClosesMinute,
            pp.Status,
            pp.Status == PickupPointStatus.Active
                && pp.Partner.Status == PartnerStatus.Active
                && minute >= pp.OpensMinute
                && minute < pp.ClosesMinute,
            pp.CreatedAt,
            pp.Version
        );

    private static IQueryable<CustomerRow> CustomerRows(IQueryable<Customer> customers) =>
        customers.Select(c => new CustomerRow(
            c.Id,
            c.DisplayName,
            c.Email,
            c.Phone,
            c.Addresses.Count,
            c.Addresses.Where(a => a.IsDefault).Select(a => a.City).FirstOrDefault(),
            c.CreatedAt,
            c.Version
        ));

    private static IQueryable<CustomerAddressRow> AddressRows(
        ShipmentDbContext db,
        IQueryable<CustomerAddress> addresses
    ) =>
        addresses.Select(a => new CustomerAddressRow(
            a.Id,
            a.CustomerId,
            db.Customers.Where(c => c.Id == a.CustomerId).Select(c => c.DisplayName).First(),
            a.Label,
            a.AddressLine,
            a.City,
            a.Postcode,
            a.Latitude,
            a.Longitude,
            a.Notes,
            a.IsDefault,
            a.CreatedAt,
            a.Version
        ));
}
