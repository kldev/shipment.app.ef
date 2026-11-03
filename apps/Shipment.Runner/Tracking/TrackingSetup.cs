using Microsoft.Extensions.Options;

namespace Shipment.Runner.Tracking;

public static class TrackingSetup
{
    /// <summary>OSRM's client with retries and timeouts from the configuration, and the demo's simulator.</summary>
    public static IServiceCollection AddTracking(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services
            .AddOptions<OsrmOptions>()
            .BindConfiguration(OsrmOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services
            .AddOptions<SimulatorOptions>()
            .BindConfiguration(SimulatorOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddHttpClient(
                OsrmClient.HttpClientName,
                (sp, http) =>
                {
                    var osrm = sp.GetRequiredService<IOptions<OsrmOptions>>().Value;
                    http.BaseAddress = new Uri(osrm.BaseUrl.TrimEnd('/') + "/");
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(osrm.UserAgent);
                }
            )
            .AddStandardResilienceHandler(resilience =>
            {
                // The pipeline is built at registration: the same section the options above validate at start.
                var osrm =
                    configuration.GetSection(OsrmOptions.SectionName).Get<OsrmOptions>()
                    ?? new OsrmOptions();
                resilience.Retry.MaxRetryAttempts = osrm.Retries;
                resilience.AttemptTimeout.Timeout = osrm.AttemptTimeout;
                resilience.TotalRequestTimeout.Timeout = osrm.TotalTimeout;
                // The breaker must sample at least two attempts' time (the handler validates it).
                resilience.CircuitBreaker.SamplingDuration = osrm.AttemptTimeout * 2;
            });

        services.AddSingleton<OsrmClient>();
        services.AddHostedService<SimulatorService>();
        return services;
    }
}
