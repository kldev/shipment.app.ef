using JetBrains.Annotations;
using Shipment.Api;
using Shipment.Api.Auth;
using Shipment.Api.Hosting;
using Shipment.Api.Web;
using Shipment.Observability.AspNetCore;

// The aspnet image has no curl: the Docker HEALTHCHECK runs `dotnet Shipment.Api.dll healthcheck`.
if (args is ["healthcheck"])
    return await ContainerHealthCheck.RunAsync();

var builder = WebApplication.CreateBuilder(args);
{
    // Serilog (levels in the "Serilog" section) and OpenTelemetry; exported only with OTEL_EXPORTER_OTLP_ENDPOINT.
    builder.AddShipmentWebObservability("shipment-api");
    builder.Services.AddProblemDetails();
    builder.AddReverseProxy();
    builder.AddShipmentDatabase();
    builder.AddShipmentAuth();
    builder.AddShipmentDataProtection();
    builder.AddShipmentApi();
    builder.AddShipmentHealthChecks();
    builder.AddShipmentFront();
}

var app = builder.Build();
{
    app.UseShipmentPipeline();
    app.MapShipmentApi();
    app.MapAuth();
    app.MapShipmentHealthChecks();
    app.MapShipmentFront();
}

await app.RunAsync();
return 0;

// The entry point class, public for WebApplicationFactory<Program> in the tests.
[UsedImplicitly]
public partial class Program;
