using Microsoft.Extensions.DependencyInjection;
using Shipment.Application.Billing;
using Shipment.Application.Carriers;
using Shipment.Application.Catalog;
using Shipment.Application.Compliance;
using Shipment.Application.Crm;
using Shipment.Application.Dispatch;
using Shipment.Application.Ordering;
using Shipment.Application.Platform;
using Shipment.Application.Tracking;

namespace Shipment.Application;

public static class ApplicationSetup
{
    /// <summary>The bounded contexts' services; the host adds <c>ICurrentActor</c> and the database.</summary>
    public static IServiceCollection AddShipmentApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<PlatformService>();
        services.AddScoped<CrmService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<CarriersService>();
        services.AddScoped<ComplianceService>();
        services.AddScoped<OrderingService>();
        services.AddScoped<PickingService>();
        services.AddScoped<DispatchService>();
        services.AddScoped<TrackingService>();
        services.AddScoped<BillingService>();
        return services;
    }
}
