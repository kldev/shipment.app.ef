using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Shipment.Application.Platform;

namespace Shipment.Api.Auth;

/// <summary>
/// /auth/login (the sign-in form posts here), /auth/demo-login (the demo's button, when DemoSignIn is on), /auth/logout
/// and /auth/session-expired. No antiforgery token: the cookie is
/// SameSite=Lax and the cross-site write guard refuses posts from other sites (as in Tech Lead Wiki).
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        // The sign-in form and its redirects are not part of the REST API.
        app = app.MapGroup("").ExcludeFromDescription();
        app.MapPost("/auth/login", LoginAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting(AuthSetup.SignInRateLimit);
        app.MapPost("/auth/demo-login", DemoLoginAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting(AuthSetup.SignInRateLimit);
        app.MapPost(
                "/auth/logout",
                async (HttpContext http) =>
                {
                    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return Results.LocalRedirect("/sign-in");
                }
            )
            .AllowAnonymous()
            .DisableAntiforgery();
        // The session ended while a page was open: sign in again.
        app.MapGet(
                "/auth/session-expired",
                async (HttpContext http) =>
                {
                    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return Results.LocalRedirect("/sign-in?error=expired");
                }
            )
            .AllowAnonymous();
        return app;
    }

    private static async Task<IResult> LoginAsync(
        HttpContext http,
        PlatformService platform,
        IOptions<SessionOptions> session,
        CancellationToken ct
    )
    {
        var form = await http.Request.ReadFormAsync(ct);
        var returnUrl = LocalUrl(form["returnUrl"]);
        var (user, failure) = await platform.VerifyAsync(form["username"], form["password"], ct);
        if (user is null)
        {
            var error = failure == SignInFailure.Inactive ? "inactive" : "invalid";
            return Results.LocalRedirect(
                $"/sign-in?error={error}&returnUrl={Uri.EscapeDataString(returnUrl)}"
            );
        }

        await StartSessionAsync(http, session.Value, user);
        return Results.LocalRedirect(returnUrl);
    }

    /// <summary>
    /// The demo's "sign in as the default admin": no password, the configured account. A 404 unless DemoSignIn is on,
    /// so outside the local demo the endpoint does not exist for anyone.
    /// </summary>
    private static async Task<IResult> DemoLoginAsync(
        HttpContext http,
        PlatformService platform,
        IOptions<SessionOptions> session,
        IOptions<DemoSignInOptions> demo,
        ILogger<DemoSignInOptions> logger,
        CancellationToken ct
    )
    {
        if (!demo.Value.Enabled)
            return Results.NotFound();
        var form = await http.Request.ReadFormAsync(ct);
        var returnUrl = LocalUrl(form["returnUrl"]);
        var user = await platform.FindActiveAsync(demo.Value.Username, ct);
        if (user is null)
            return Results.LocalRedirect(
                $"/sign-in?error=demo&returnUrl={Uri.EscapeDataString(returnUrl)}"
            );
        logger.LogInformation("Demo sign-in as {Username}", user.Username);
        await StartSessionAsync(http, session.Value, user);
        return Results.LocalRedirect(returnUrl);
    }

    private static async Task StartSessionAsync(
        HttpContext http,
        SessionOptions session,
        SignInUser user
    )
    {
        var expires = DateTimeOffset.UtcNow.Add(session.Lifetime);
        var identity = new ClaimsIdentity(
            [
                new Claim(SessionClaims.UserId, user.Id.ToString()),
                new Claim(SessionClaims.Name, user.DisplayName),
                new Claim(SessionClaims.Username, user.Username),
                new Claim(SessionClaims.Email, user.Email),
                new Claim(SessionClaims.Role, user.Role),
                new Claim(
                    SessionClaims.Expires,
                    expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
                ),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme,
            SessionClaims.Name,
            SessionClaims.Role
        );
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { ExpiresUtc = expires }
        );
    }

    /// <summary>Only a path of this site: an absolute URL in returnUrl would be an open redirect.</summary>
    public static string LocalUrl(string? url) =>
        url is { Length: > 1 }
        && url.StartsWith('/')
        && !url.StartsWith("//")
        && !url.StartsWith("/\\")
            ? url
            : "/dashboard";
}
