namespace Shipment.Domain.Common;

/// <summary>How big a product is, S &lt; M &lt; L &lt; XL - it chooses the vehicle.</summary>
public enum SizeClass
{
    S = 1,
    M = 2,
    L = 3,

    // ReSharper disable once InconsistentNaming - the size as people write it (and as it is stored).
    XL = 4,
}
