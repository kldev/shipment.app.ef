using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Shipment.Api.Auth;
using Shipment.Api.Components;
using Shipment.Api.Endpoints;
using Shipment.Api.Health;
using Shipment.Api.Web;
using Shipment.Application;
using Shipment.Domain.Common;
using Shipment.Observability.AspNetCore;
using Shipment.Persistence;

namespace Shipment.Api.Hosting;

/// <summary>
/// The host's parts as extension methods, so Program.cs reads as the list of what the host is: the services in one
/// block, the pipeline and the endpoints in another (as Ambit Plans'). Feature areas keep their own setup next to
/// their code (Auth/, Messaging/, Web/); this file holds the host's common parts.
/// </summary>
public static class HostingSetup
{
    /// <summary>One data source and a DbContext factory for the whole host (shipment_api), and the application services.</summary>
    public static WebApplicationBuilder AddShipmentDatabase(this WebApplicationBuilder builder)
    {
        var connectionString =
            builder.Configuration.GetConnectionString("shipment")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:shipment is not set (user-secrets for dotnet run - deploy/deploy.sh writes them; environment in the container)."
            );
        builder.Services.AddShipmentDatabase(connectionString, "shipment");
        builder.Services.AddShipmentApplication();
        return builder;
    }

    /// <summary>
    /// Session cookies are encrypted with these keys: on a volume in the container (deploy.sh), so a redeploy keeps
    /// sessions.
    /// </summary>
    public static WebApplicationBuilder AddShipmentDataProtection(
        this WebApplicationBuilder builder
    )
    {
        var dataProtection = builder.Services.AddDataProtection().SetApplicationName("shipment");
        if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        return builder;
    }

    /// <summary>The REST API's OpenAPI document (/openapi/v1.json) and enums as their snake_case names.</summary>
    public static WebApplicationBuilder AddShipmentApi(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            // Sizes as people write them and as they are stored (S, M, L, XL); the first converter that fits wins.
            o.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter<SizeClass>(allowIntegerValues: false)
            );
            o.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(
                    JsonNamingPolicy.SnakeCaseLower,
                    allowIntegerValues: false
                )
            );
        });
        builder.Services.AddOpenApi(
            ApiRoutes.Document,
            o =>
                o.AddDocumentTransformer(
                    (document, _, _) =>
                    {
                        document.Info.Title = "Shipment API";
                        document.Info.Version = ApiRoutes.Document;
                        document.Info.Description =
                            "The back office of a delivery operator: partners and their pickup points, customers, catalogs, couriers and their documents, orders and picking, dispatch, tracking and billing. Every call needs the session cookie of a signed-in admin.";
                        return Task.CompletedTask;
                    }
                )
        );
        return builder;
    }

    public static WebApplicationBuilder AddShipmentHealthChecks(this WebApplicationBuilder builder)
    {
        builder
            .Services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: [HealthTags.Ready]);
        return builder;
    }

    /// <summary>The front: Blazor, interactive server, in this same host (ADR 0002).</summary>
    public static WebApplicationBuilder AddShipmentFront(this WebApplicationBuilder builder)
    {
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddCascadingAuthenticationState();
        // After the defaults: the circuit re-checks the session every minute.
        builder.Services.AddScoped<AuthenticationStateProvider, SessionRevalidation>();
        builder.Services.AddScoped<CurrentUser>();
        return builder;
    }

    /// <summary>The middleware, in this order.</summary>
    public static WebApplication UseShipmentPipeline(this WebApplication app)
    {
        // First: the request logging, the session cookie and the rate limiter see the browser's scheme and address.
        app.UseReverseProxy();
        // Before the exception handler: an unhandled exception is logged with its request, not as a plain 500.
        app.UseShipmentRequestLogging();
        // A request that does not bind (a bad enum name, an unreadable body) keeps its 400 - in Development Minimal APIs
        // throw it instead of answering, and the handler would make it a 500.
        app.UseExceptionHandler(
            new ExceptionHandlerOptions
            {
                StatusCodeSelector = exception =>
                    exception is BadHttpRequestException bad
                        ? bad.StatusCode
                        : StatusCodes.Status500InternalServerError,
            }
        );
        // Pages only: an API status keeps its JSON (or empty) body instead of the Blazor "not found" page.
        app.UseWhen(
            http => !AuthSetup.IsApi(http.Request.Path),
            pages =>
                pages.UseStatusCodePagesWithReExecute(
                    "/not-found",
                    createScopeForStatusCodePages: true
                )
        );
        app.UseCrossSiteWriteGuard();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        return app;
    }

    /// <summary>The REST API (every group under Endpoints/) and its OpenAPI document.</summary>
    public static void MapShipmentApi(this WebApplication app)
    {
        app.MapOpenApi().AllowAnonymous();
        app.MapEndpoints();
    }

    public static void MapShipmentHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();
        app.MapHealthChecks(
                "/health/ready",
                new HealthCheckOptions
                {
                    Predicate = check => check.Tags.Contains(HealthTags.Ready),
                    ResponseWriter = HealthResponse.WriteAsync,
                }
            )
            .AllowAnonymous();
    }

    /// <summary>
    /// The API reference, the pages and the API's 404s. The reference sits behind the sign-in (as in Tech Lead Wiki);
    /// nothing leaves the machine: no fonts from Scalar's CDN, no telemetry, no AI agent.
    /// In Shipment's palette.
    /// </summary>
    public static void MapShipmentFront(this WebApplication app)
    {
        app.MapScalarApiReference(o =>
            o.WithTitle("Shipment API")
                .AddDocument(
                    ApiRoutes.Document,
                    "Shipment API",
                    $"/openapi/{ApiRoutes.Document}.json"
                )
                .DisableDefaultFonts()
                .DisableTelemetry()
                .DisableAgent()
                .DisableMcp()
                .ForceDarkMode()
                .HideDarkModeToggle()
                .WithCustomCss(ScalarPalette.Css)
        );
        // Assets carry no data and the sign-in page needs them.
        app.MapStaticAssets().AllowAnonymous();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        // An unknown /api/x must stay a 404, not become the Blazor "not found" page with 200.
        foreach (var prefix in AuthSetup.ApiPrefixes)
            app.MapFallback($"{prefix}/{{**path}}", () => Results.NotFound()).AllowAnonymous();
    }
}
