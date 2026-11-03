using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shipment.Domain.Common;

namespace Shipment.Api.Auth;

/// <summary>Claims of the session cookie.</summary>
public static class SessionClaims
{
    public const string UserId = "sub";
    public const string Name = "name";
    public const string Username = "preferred_username";
    public const string Email = "email";
    public const string Role = "role";

    /// <summary>When the session ends (unix seconds) - the circuit checks it (SessionRevalidation).</summary>
    public const string Expires = "exp";

    public static Guid? UserIdOf(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirstValue(UserId), out var id) ? id : null;

    public static DateTimeOffset? ExpiresOf(ClaimsPrincipal user) =>
        long.TryParse(user.FindFirstValue(Expires), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}

/// <summary>
/// One host, one way in (ADR 0002): the session cookie (shipment.auth). The pages call the application services in
/// the same process; the REST API under /api answers with 401/403 instead of redirecting to the sign-in page.
/// </summary>
public static class AuthSetup
{
    private const string CookieName = "shipment.auth";
    public const string SignInRateLimit = "sign-in";

    /// <summary>Paths that answer with a status and a JSON body, never with a page.</summary>
    public static readonly string[] ApiPrefixes = ["/api", "/openapi", "/auth"];

    public static bool IsApi(PathString path) => ApiPrefixes.Any(p => path.StartsWithSegments(p));

    public static WebApplicationBuilder AddShipmentAuth(this WebApplicationBuilder builder)
    {
        builder
            .Services.AddOptions<SessionOptions>()
            .BindConfiguration(SessionOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder
            .Services.AddOptions<DemoSignInOptions>()
            .BindConfiguration(DemoSignInOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder
            .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie();
        builder
            .Services.AddOptions<CookieAuthenticationOptions>(
                CookieAuthenticationDefaults.AuthenticationScheme
            )
            .Configure<IOptions<SessionOptions>>(
                (cookie, session) =>
                {
                    cookie.Cookie.Name = CookieName;
                    cookie.Cookie.HttpOnly = true;
                    cookie.Cookie.SameSite = SameSiteMode.Lax;
                    cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    cookie.ExpireTimeSpan = session.Value.Lifetime;
                    cookie.SlidingExpiration = false;
                    cookie.LoginPath = "/sign-in";
                    cookie.AccessDeniedPath = "/access-denied";
                    // The API answers with a status; pages and Scalar redirect to the sign-in page.
                    cookie.Events.OnRedirectToLogin = ctx => StatusOrRedirect(ctx, 401);
                    cookie.Events.OnRedirectToAccessDenied = ctx => StatusOrRedirect(ctx, 403);
                }
            );

        builder
            .Services.AddAuthorizationBuilder()
            // Everything needs a signed-in user unless it says otherwise (sign-in page, assets, /health).
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        // Password guessing: a few attempts per minute per client address; the form comes back with a message.
        var attempts =
            builder
                .Configuration.GetSection(SessionOptions.SectionName)
                .Get<SessionOptions>()
                ?.SignInAttemptsPerMinute
            ?? 10;
        builder.Services.AddRateLimiter(o =>
        {
            o.OnRejected = (ctx, _) =>
            {
                ctx.HttpContext.Response.Redirect("/sign-in?error=throttled");
                return ValueTask.CompletedTask;
            };
            o.AddPolicy(
                SignInRateLimit,
                http =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = attempts,
                            Window = TimeSpan.FromMinutes(1),
                        }
                    )
            );
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<CurrentActor>();
        builder.Services.AddScoped<ICurrentActor>(sp => sp.GetRequiredService<CurrentActor>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped<CircuitHandler, ActorCircuitHandler>()
        );
        return builder;
    }

    private static Task StatusOrRedirect(
        RedirectContext<CookieAuthenticationOptions> ctx,
        int status
    )
    {
        if (IsApi(ctx.Request.Path))
            ctx.Response.StatusCode = status;
        else
            ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes from another site are refused (Tech Lead Wiki's filter): the session cookie would ride along with a form
    /// or a fetch from another origin. Browsers send Sec-Fetch-Site and Origin; curl and tests send neither and pass.
    /// </summary>
    public static IApplicationBuilder UseCrossSiteWriteGuard(this IApplicationBuilder app) =>
        app.Use(
            async (context, next) =>
            {
                var request = context.Request;
                if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
                {
                    var site = request.Headers["Sec-Fetch-Site"].ToString();
                    var origin = request.Headers.Origin.ToString();
                    var foreignOrigin =
                        origin.Length > 0
                        && (
                            !Uri.TryCreate(origin, UriKind.Absolute, out var url)
                            || !string.Equals(
                                url.Host,
                                request.Host.Host,
                                StringComparison.OrdinalIgnoreCase
                            )
                        );
                    if (site is "cross-site" or "same-site" || foreignOrigin)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }
                }
                await next(context);
            }
        );
}
