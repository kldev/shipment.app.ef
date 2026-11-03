using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Shipment.Domain.Common;

namespace Shipment.Api.Auth;

/// <summary>
/// The acting admin of this scope: the request's user for the API and the first render, the circuit's user for an
/// interactive page (a circuit has no HttpContext - the handler below hands the user over).
/// </summary>
public sealed class CurrentActor(IHttpContextAccessor http) : ICurrentActor
{
    internal ClaimsPrincipal? CircuitUser { get; set; }

    public Guid? UserId => SessionClaims.UserIdOf(CircuitUser ?? http.HttpContext?.User);
}

/// <summary>Gives the circuit's signed-in user to <see cref="CurrentActor"/> (the documented Blazor Server pattern).</summary>
internal sealed class ActorCircuitHandler(
    CurrentActor actor,
    AuthenticationStateProvider authentication
) : CircuitHandler, IDisposable
{
    public override async Task OnCircuitOpenedAsync(
        Circuit circuit,
        CancellationToken cancellationToken
    )
    {
        actor.CircuitUser = (await authentication.GetAuthenticationStateAsync()).User;
        authentication.AuthenticationStateChanged += OnChanged;
    }

    public void Dispose() => authentication.AuthenticationStateChanged -= OnChanged;

    private async void OnChanged(Task<AuthenticationState> state)
    {
        try
        {
            actor.CircuitUser = (await state).User;
        }
        catch (Exception)
        {
            actor.CircuitUser = null;
        }
    }
}
