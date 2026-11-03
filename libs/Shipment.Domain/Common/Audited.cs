namespace Shipment.Domain.Common;

/// <summary>
/// A row with who made and last changed it, and the row version for optimistic concurrency (PostgreSQL's xmin). The
/// persistence stamps the four columns on save from the acting user; the runner acts for the system (no user).
/// </summary>
public abstract class Audited
{
    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    /// <summary>xmin: changes with every update of the row; the client sends back the one it read.</summary>
    public uint Version { get; set; }
}
