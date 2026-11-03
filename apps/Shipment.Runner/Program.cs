using JasperFx;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Shipment.Application;
using Shipment.Domain.Common;
using Shipment.Observability;
using Shipment.Persistence;
using Shipment.Runner.Messaging;
using Shipment.Runner.Outbox;
using Shipment.Runner.Tracking;

var builder = WebApplication.CreateBuilder(args);

// Logs, traces and metrics; shipped over OTLP only when OTEL_EXPORTER_OTLP_ENDPOINT is set.
builder.AddShipmentObservability("shipment-runner");

var connectionString = RunnerDatabase.Normalize(
    builder.Configuration.GetConnectionString(RunnerDatabase.ConnectionStringName)
        ?? throw new InvalidOperationException(
            $"ConnectionStrings:{RunnerDatabase.ConnectionStringName} is not set (shipment_runner; deploy/deploy.sh writes it)."
        )
);
builder.Services.AddShipmentDatabase(connectionString, "shipment-runner");
builder.Services.AddShipmentApplication();
builder.Services.AddSingleton<ICurrentActor, SystemActor>();

builder.Services.AddOutboxRelay();
builder.Services.AddTracking(builder.Configuration);
builder.AddRunnerMessaging(connectionString);

builder.Services.AddHealthChecks().AddRunnerChecks();

var app = builder.Build();

// Liveness: the process answers. Readiness: PostgreSQL and RabbitMQ are reachable.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains(RunnerHealth.Ready) }
);

// Runs the host, or a Wolverine command instead: `describe` lists the handlers.
return await app.RunJasperFxCommands(args);

// The entry point class, public for WebApplicationFactory<Program> in the tests.
[UsedImplicitly]
public partial class Program;
