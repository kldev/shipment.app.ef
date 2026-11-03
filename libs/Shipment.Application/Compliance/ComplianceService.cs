using Microsoft.EntityFrameworkCore;
using Shipment.Application.Common;
using Shipment.Domain.Carriers;
using Shipment.Domain.Common;
using Shipment.Domain.Compliance;
using Shipment.Persistence;

namespace Shipment.Application.Compliance;

public sealed record DocumentRow(
    Guid Id,
    Guid CourierId,
    string CourierName,
    DocumentKind Kind,
    string Number,
    DateOnly ValidUntil,
    int DaysLeft,
    DocumentState State,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset CreatedAt,
    uint RowVersion
);

public sealed record ClearanceRow(
    Guid CourierId,
    string DisplayName,
    string VehicleType,
    ShiftStatus ShiftStatus,
    bool Cleared,
    IReadOnlyList<string> Reasons,
    DateOnly? NextExpiry,
    bool ExpiringSoon
);

/// <summary>Whether a set of products needs an ID check at the door, and which of them (by name).</summary>
public sealed record AgeCheckResult(bool IdCheckRequired, IReadOnlyList<string> RestrictedProducts);

public sealed record SetDocument(
    Guid Id,
    Guid CourierId,
    DocumentKind Kind,
    string Number,
    DateOnly ValidUntil
);

public sealed record VerifyDocument(Guid Id, uint RowVersion);

/// <summary>
/// The compliance context: the couriers' documents and whether a courier may take orders. Days are the operator's
/// (Europe/Warsaw).
/// </summary>
public sealed class ComplianceService(
    IDbContextFactory<ShipmentDbContext> factory,
    TimeProvider time,
    ICurrentActor actor
)
{
    private const string DocumentName = "Document";

    public async Task<IReadOnlyList<DocumentRow>> ListDocumentsAsync(
        Guid? courierId = null,
        DocumentKind? kind = null,
        DocumentState? state = null,
        CancellationToken ct = default
    )
    {
        var today = OperatorTime.Today(time.GetUtcNow());
        await using var db = await factory.CreateDbContextAsync(ct);
        var documents = db.CourierDocuments.AsQueryable();
        if (courierId is { } courier)
            documents = documents.Where(d => d.CourierId == courier);
        if (kind is { } wanted)
            documents = documents.Where(d => d.Kind == wanted);
        var rows = await documents
            .Join(db.Couriers, d => d.CourierId, c => c.Id, (d, c) => new { d, c.DisplayName })
            .OrderBy(x => x.d.ValidUntil)
            .Select(x => new
            {
                x.d.Id,
                x.d.CourierId,
                x.DisplayName,
                x.d.Kind,
                x.d.Number,
                x.d.ValidUntil,
                x.d.VerifiedAt,
                x.d.CreatedAt,
                x.d.Version,
            })
            .ToListAsync(ct);
        return rows.Select(r => new DocumentRow(
                r.Id,
                r.CourierId,
                r.DisplayName,
                r.Kind,
                r.Number,
                r.ValidUntil,
                r.ValidUntil.DayNumber - today.DayNumber,
                CourierDocument.StateOf(r.ValidUntil, r.VerifiedAt is not null, today),
                r.VerifiedAt,
                r.CreatedAt,
                r.Version
            ))
            .Where(r => state is null || r.State == state)
            .ToList();
    }

    public async Task<IReadOnlyList<ClearanceRow>> ListClearanceAsync(
        Guid? courierId = null,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ClearanceAsync(
            db,
            courierId is { } id ? [id] : null,
            OperatorTime.Today(time.GetUtcNow()),
            ct
        );
    }

    /// <summary>
    /// The clearance of the couriers (all of them, or the given ones) - also dispatch's proposals and assignment read it.
    /// </summary>
    internal static async Task<List<ClearanceRow>> ClearanceAsync(
        ShipmentDbContext db,
        List<Guid>? courierIds,
        DateOnly today,
        CancellationToken ct
    )
    {
        var couriers = db.Couriers.AsQueryable();
        if (courierIds is not null)
            couriers = couriers.Where(c => courierIds.Contains(c.Id));
        var rows = await couriers
            .Join(
                db.VehicleTypes,
                c => c.VehicleType,
                v => v.Code,
                (c, v) => new { c, v.NeedsLicence }
            )
            .OrderBy(x => x.c.DisplayName)
            .Select(x => new
            {
                x.c.Id,
                x.c.DisplayName,
                x.c.VehicleType,
                x.c.ShiftStatus,
                x.NeedsLicence,
                Documents = db
                    .CourierDocuments.Where(d => d.CourierId == x.c.Id)
                    .Select(d => new DocumentFacts(d.Kind, d.ValidUntil, d.VerifiedAt != null))
                    .ToList(),
            })
            .ToListAsync(ct);
        return rows.Select(r =>
            {
                var clearance = Clearance.Of(r.NeedsLicence, r.Documents, today);
                return new ClearanceRow(
                    r.Id,
                    r.DisplayName,
                    r.VehicleType,
                    r.ShiftStatus,
                    clearance.Cleared,
                    clearance.Reasons,
                    clearance.NextExpiry,
                    clearance.ExpiringSoon
                );
            })
            .ToList();
    }

    /// <summary>
    /// Records a courier's document. One per kind: a renewal replaces the current one (it keeps its id) and needs
    /// verifying again when the number or the date changed. A new kind takes the id from the page (ADR 0009).
    /// </summary>
    public Task<Result<uint>> SetDocumentAsync(
        SetDocument command,
        CancellationToken ct = default
    ) => Races.OnceMoreOnConflictAsync(() => SetDocumentOnceAsync(command, ct));

    private async Task<Result<uint>> SetDocumentOnceAsync(SetDocument command, CancellationToken ct)
    {
        if (command.Id == Guid.Empty)
            return Error.Validation("Document id is required.");
        if (!Enum.IsDefined(command.Kind))
            return Error.Validation(
                "Document must be an ID card, a driving licence, vehicle insurance or a food hygiene certificate."
            );
        var number = Text.Clean(command.Number)?.ToUpperInvariant();
        if (number is null)
            return Error.Validation("Document number is required.");
        if (command.ValidUntil == default)
            return Error.Validation("Say until when the document is valid.");

        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Couriers.AnyAsync(c => c.Id == command.CourierId, ct))
            return Error.NotFound("Courier not found.");
        var current = await db.CourierDocuments.FirstOrDefaultAsync(
            d => d.CourierId == command.CourierId && d.Kind == command.Kind,
            ct
        );
        if (current is not null)
        {
            if (!current.Renew(number, command.ValidUntil))
                return current.Version;
            return await db.SaveAsync(DocumentName, ct) is { Error: { } renewError }
                ? renewError
                : current.Version;
        }
        if (await db.CourierDocuments.AnyAsync(d => d.Id == command.Id, ct))
            return Error.Conflict("This id is already used by another document.");

        var document = new CourierDocument(
            command.Id,
            command.CourierId,
            command.Kind,
            number,
            command.ValidUntil
        );
        db.CourierDocuments.Add(document);
        return await db.SaveAsync(DocumentName, ct) is { Error: { } error }
            ? error
            : document.Version;
    }

    /// <summary>An admin saw it; it counts while valid. A document renewed meanwhile must be looked at again (409).</summary>
    public async Task<Result<uint>> VerifyDocumentAsync(
        VerifyDocument command,
        CancellationToken ct = default
    )
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var document = await db.CourierDocuments.FindAsync([command.Id], ct);
        if (document is null)
            return Error.NotFound("Document not found.");
        if (!db.Expect(document, command.RowVersion))
            return Error.Conflict(
                "Document was changed by someone else. Reload it and look at it again."
            );
        if (!document.Verify(actor.UserId, time.GetUtcNow()))
            return document.Version;
        return await db.SaveAsync(DocumentName, ct) is { Error: { } error }
            ? error
            : document.Version;
    }

    /// <summary>The 18+ products among the given ones (alcohol, tobacco - catalog's age restricted flag).</summary>
    public async Task<AgeCheckResult> AgeCheckAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken ct = default
    )
    {
        var ids = productIds.ToList();
        await using var db = await factory.CreateDbContextAsync(ct);
        var restricted = await db
            .Products.Where(p => ids.Contains(p.Id) && p.AgeRestricted)
            .OrderBy(p => p.Name)
            .Select(p => p.Name)
            .ToListAsync(ct);
        return new AgeCheckResult(restricted.Count > 0, restricted);
    }
}
