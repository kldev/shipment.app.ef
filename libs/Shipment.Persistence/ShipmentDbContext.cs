using Microsoft.EntityFrameworkCore;
using Shipment.Domain.Billing;
using Shipment.Domain.Carriers;
using Shipment.Domain.Catalog;
using Shipment.Domain.Common;
using Shipment.Domain.Compliance;
using Shipment.Domain.Crm;
using Shipment.Domain.Dispatch;
using Shipment.Domain.Messaging;
using Shipment.Domain.Ordering;
using Shipment.Domain.Platform;
using Shipment.Domain.Tracking;

namespace Shipment.Persistence;

/// <summary>
/// One database, one context; each bounded context keeps its own schema (ADR 0008). Names are snake_case
/// (crm.pickup_points.opens_minute), enums are stored as their snake_case names, money in bigint grosze.
/// </summary>
public sealed class ShipmentDbContext(DbContextOptions<ShipmentDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Partner> Partners => Set<Partner>();

    public DbSet<PickupPoint> PickupPoints => Set<PickupPoint>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<VehicleType> VehicleTypes => Set<VehicleType>();

    public DbSet<Courier> Couriers => Set<Courier>();

    public DbSet<CourierDocument> CourierDocuments => Set<CourierDocument>();

    public DbSet<DeliveryFeeBand> DeliveryFeeBands => Set<DeliveryFeeBand>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public DbSet<Delivery> Shipments => Set<Delivery>();

    public DbSet<Route> Routes => Set<Route>();

    public DbSet<Ping> Pings => Set<Ping>();

    public DbSet<PayoutRate> PayoutRates => Set<PayoutRate>();

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Human codes: ORD-104001, SHP-204001.
        modelBuilder.HasSequence<long>(Codes.OrderSequence, "ordering").StartsAt(104001);
        modelBuilder.HasSequence<long>(Codes.ShipmentSequence, "dispatch").StartsAt(204001);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShipmentDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<SizeClass>().HaveConversion<string>().HaveMaxLength(2);
        Snake<UserStatus>(configurationBuilder);
        Snake<PartnerKind>(configurationBuilder);
        Snake<PartnerStatus>(configurationBuilder);
        Snake<PickupPointStatus>(configurationBuilder);
        Snake<ShiftStatus>(configurationBuilder);
        Snake<DocumentKind>(configurationBuilder, 30);
        Snake<OrderStatus>(configurationBuilder);
        Snake<ShipmentStatus>(configurationBuilder);
        Snake<RouteLeg>(configurationBuilder, 12);
        Snake<EntryKind>(configurationBuilder);
    }

    private static void Snake<TEnum>(ModelConfigurationBuilder builder, int maxLength = 20)
        where TEnum : struct, Enum =>
        builder
            .Properties<TEnum>()
            .HaveConversion<SnakeCaseEnumConverter<TEnum>>()
            .HaveMaxLength(maxLength);
}
