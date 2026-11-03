using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;

namespace Shipment.Api.Auth;

/// <summary>The signed-in session (the cookie shipment.auth). Section "Session".</summary>
// Set by the configuration binder (BindConfiguration).
[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public sealed class SessionOptions
{
    public const string SectionName = "Session";

    /// <summary>How long a session lasts from sign-in (no sliding). Session__Lifetime.</summary>
    [Range(typeof(TimeSpan), "00:05:00", "1.00:00:00")]
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromHours(8);

    /// <summary>Sign-in attempts per minute per client address (password guessing). Session__SignInAttemptsPerMinute.</summary>
    [Range(1, 10_000)]
    public int SignInAttemptsPerMinute { get; init; } = 10;
}
