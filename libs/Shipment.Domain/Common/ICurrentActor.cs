namespace Shipment.Domain.Common;

/// <summary>Who acts in this scope: the signed-in admin, or nobody (the runner acting for the system).</summary>
public interface ICurrentActor
{
    Guid? UserId { get; }
}
