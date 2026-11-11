using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Shipment.Application.Platform;

namespace Shipment.Api.Auth;

/// <summary>
/// An open Blazor circuit outlives the request that started it: every minute it checks that the session has not
/// expired (the cookie's lifetime, kept as a claim) and that the account is still active. When not, the circuit's user
/// becomes anonymous and the router sends the page to the sign-in (before ADR 0013 the expired access token did it).
/// </summary>
internal sealed class SessionRevalidation(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopes,
    TimeProvider time
) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken
    )
    {
        var user = authenticationState.User;
        if (SessionClaims.ExpiresOf(user) is not { } expires || expires <= time.GetUtcNow())
            return false;
        if (SessionClaims.UserIdOf(user) is not { } id)
            return false;
        await using var scope = scopes.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformService>();
        return await platform.GetUserAsync(id, cancellationToken) is not null;
    }
}
