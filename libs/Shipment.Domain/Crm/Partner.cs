using Shipment.Domain.Common;

namespace Shipment.Domain.Crm;

public enum PartnerKind
{
    Grocery,
    Restaurant,
    Pharmacy,
    Shop,
}

public enum PartnerStatus
{
    Active,
    Paused,
}

/// <summary>A store or restaurant that hands orders over at its pickup points; pays a commission on the goods.</summary>
public sealed class Partner : Audited
{
    public const int MaxCommissionBps = 5000;

    private Partner() { }

    public Partner(Guid id, string code, string name, PartnerKind kind, int commissionBps)
    {
        Id = id;
        Code = code;
        Name = name;
        Kind = kind;
        CommissionBps = commissionBps;
        Status = PartnerStatus.Active;
    }

    public Guid Id { get; private set; }

    /// <summary>2-12 capital letters or digits, unique.</summary>
    public string Code { get; private set; } = "";

    public string Name { get; private set; } = "";

    public PartnerKind Kind { get; private set; }

    /// <summary>The operator's commission on the goods in basis points (1500 = 15 %).</summary>
    public int CommissionBps { get; private set; }

    public PartnerStatus Status { get; private set; }

    public bool Same(string code, string name, PartnerKind kind, int commissionBps) =>
        Code == code && Name == name && Kind == kind && CommissionBps == commissionBps;

    /// <summary>False when nothing changes (a no-op leaves no trace).</summary>
    public bool Update(
        string code,
        string name,
        PartnerKind kind,
        int commissionBps,
        PartnerStatus status
    )
    {
        if (Same(code, name, kind, commissionBps) && Status == status)
            return false;
        (Code, Name, Kind, CommissionBps, Status) = (code, name, kind, commissionBps, status);
        return true;
    }
}
