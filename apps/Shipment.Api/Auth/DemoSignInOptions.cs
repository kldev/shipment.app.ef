using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;

namespace Shipment.Api.Auth;

/// <summary>
/// "Sign in as the default admin" without a password - for the local demo only (Shipment is never deployed anywhere,
/// PRODUCT.md). Off unless the environment turns it on: DemoSignIn__Enabled=true (deploy.sh, appsettings.Development).
/// Section "DemoSignIn".
/// </summary>
// Set by the configuration binder (BindConfiguration).
[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public sealed class DemoSignInOptions
{
    public const string SectionName = "DemoSignIn";

    public bool Enabled { get; init; }

    /// <summary>The account the button signs in as (it must exist and be active). DemoSignIn__Username.</summary>
    [Required, MinLength(2)]
    public string Username { get; init; } = "k.lopuch";
}
