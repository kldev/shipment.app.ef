namespace Shipment.Domain.Compliance;

/// <summary>A courier's document as clearance sees it.</summary>
public sealed record DocumentFacts(DocumentKind Kind, DateOnly ValidUntil, bool Verified);

/// <summary>Whether a courier may take orders, the reasons when not, and what expires next.</summary>
public sealed record ClearanceResult(
    bool Cleared,
    IReadOnlyList<string> Reasons,
    DateOnly? NextExpiry,
    bool ExpiringSoon
);

/// <summary>
/// Every courier needs a verified, valid ID card; a car or a van also a driving licence and vehicle insurance. An
/// expired document blocks assignment, not the shift.
/// </summary>
public static class Clearance
{
    // In the order the reasons are listed.
    private static readonly (DocumentKind Kind, string Label, bool LicenceOnly)[] Required =
    [
        (DocumentKind.DrivingLicence, "Driving licence", true),
        (DocumentKind.IdCard, "ID card", false),
        (DocumentKind.VehicleInsurance, "Vehicle insurance", true),
    ];

    public static ClearanceResult Of(
        bool needsLicence,
        IReadOnlyCollection<DocumentFacts> documents,
        DateOnly today
    )
    {
        var reasons = new List<string>();
        foreach (var (kind, label, licenceOnly) in Required)
        {
            if (licenceOnly && !needsLicence)
                continue;
            var document = documents.FirstOrDefault(d => d.Kind == kind);
            if (document is null)
                reasons.Add($"{label} missing");
            else if (document.ValidUntil < today)
                reasons.Add($"{label} expired");
            else if (!document.Verified)
                reasons.Add($"{label} not verified");
        }

        var valid = documents.Where(d => d.ValidUntil >= today).ToList();
        return new ClearanceResult(
            reasons.Count == 0,
            reasons,
            valid.Count == 0 ? null : valid.Min(d => d.ValidUntil),
            valid.Any(d => d.ValidUntil < today.AddDays(CourierDocument.ExpiringDays))
        );
    }
}
