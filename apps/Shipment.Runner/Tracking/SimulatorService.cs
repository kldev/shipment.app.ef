using JetBrains.Annotations;
using Microsoft.Extensions.Options;
using Shipment.Application.Carriers;
using Shipment.Application.Tracking;
using Shipment.Geo;

namespace Shipment.Runner.Tracking;

/// <summary>
/// The demo's couriers (there are no phones): every interval each simulated shipment on the way moves one step along
/// its current leg - a ping in tracking, the last position in carriers, each module's own write. At the end of a leg
/// it waits for the admin's status button.
/// </summary>
// Created by the host (AddHostedService in TrackingSetup).
[UsedImplicitly]
internal sealed class SimulatorService(
    IServiceScopeFactory scopes,
    IOptions<SimulatorOptions> options,
    TimeProvider timeProvider,
    ILogger<SimulatorService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StepAllAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The database is away: the couriers stand still until the next pass.
                logger.LogError(exception, "Simulator pass failed");
            }

            await Task.Delay(options.Value.Interval, timeProvider, stoppingToken);
        }
    }

    private async Task StepAllAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var tracking = scope.ServiceProvider.GetRequiredService<TrackingService>();
        var carriers = scope.ServiceProvider.GetRequiredService<CarriersService>();
        foreach (var step in await tracking.SimulationDueAsync(ct))
        {
            var (next, point) = GeoLine
                .Parse(step.Line)
                .Step(step.LegDistanceM, step.TravelledM, options.Value.StepMetres);
            await tracking.RecordPingAsync(
                step.ShipmentId,
                step.Leg,
                next,
                (decimal)point.Latitude,
                (decimal)point.Longitude,
                ct
            );
            await carriers.ReportPositionAsync(
                step.CourierId,
                (decimal)point.Latitude,
                (decimal)point.Longitude,
                ct
            );
        }
    }
}
