using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;

namespace Shipment.Runner.Tracking;

/// <summary>
/// OSRM, the router of the routes (docs/domain.md, "Map"). Bound from "Osrm" (Osrm__BaseUrl, ...). The defaults are the
/// public FOSSGIS server, which has a bike and a car profile under their own paths; its usage policy asks for few
/// requests and a User-Agent that names the application - the runner asks twice per assignment.
/// </summary>
// Set by the configuration binder.
[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public sealed class OsrmOptions
{
    public const string SectionName = "Osrm";

    [Required, Url]
    public string BaseUrl { get; init; } = "https://routing.openstreetmap.de";

    /// <summary>The path of the bike profile on the server (/{path}/route/v1/driving/...).</summary>
    [Required]
    public string BikePath { get; init; } = "routed-bike";

    /// <summary>The path of the car profile - cars and vans.</summary>
    [Required]
    public string CarPath { get; init; } = "routed-car";

    [Required]
    public string UserAgent { get; init; } = "Shipment/1.0 (local learning project; demo routes)";

    /// <summary>One request; the retries come on top of it.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>A leg with its retries; after it the route is drawn as a straight line (approximate).</summary>
    [Range(typeof(TimeSpan), "00:00:02", "00:05:00")]
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromSeconds(20);

    [Range(0, 10)]
    public int Retries { get; init; } = 2;
}

/// <summary>
/// The demo's simulator: couriers moved along their routes (there are no real phones). Bound from "Simulator"
/// (Simulator__Interval, ...). A shipment moves only when the admin switched it to simulate on its page.
/// </summary>
// Set by the configuration binder.
[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public sealed class SimulatorOptions
{
    public const string SectionName = "Simulator";

    public bool Enabled { get; init; } = true;

    /// <summary>The time between two steps.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Metres per step - 150 every 3 s is about 180 km/h, so a demo trip takes a minute or two.</summary>
    [Range(10, 5000)]
    public int StepMetres { get; init; } = 150;
}
