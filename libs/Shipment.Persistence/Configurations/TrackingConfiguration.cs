using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipment.Domain.Carriers;
using Shipment.Domain.Dispatch;
using Shipment.Domain.Tracking;

namespace Shipment.Persistence.Configurations;

internal sealed class RouteConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> builder)
    {
        builder.ToTable("routes", "tracking");
        builder.HasKey(r => r.ShipmentId);
        builder.Property(r => r.Profile).HasMaxLength(10);
        builder.Property(r => r.ToPickup).HasColumnType("jsonb");
        builder.Property(r => r.ToCustomer).HasColumnType("jsonb");
        builder.Check("profile", "profile IN ('bike', 'car')");
        builder.Check("sim_leg", $"sim_leg IS NULL OR {Mapping.In<RouteLeg>("sim_leg")}");
        builder.Check(
            "legs",
            "jsonb_typeof(to_pickup -> 'coordinates') = 'array' AND jsonb_typeof(to_customer -> 'coordinates') = 'array'"
        );
        builder.HasOne<Delivery>().WithOne().HasForeignKey<Route>(r => r.ShipmentId);
        builder.HasOne<Courier>().WithMany().HasForeignKey(r => r.CourierId);
        builder.HasIndex(r => r.CourierId);
        // The simulator's pass: the routes it moves.
        builder.HasIndex(r => r.ShipmentId, "ix_routes_simulated").HasFilter("simulated");
    }
}

internal sealed class PingConfiguration : IEntityTypeConfiguration<Ping>
{
    public void Configure(EntityTypeBuilder<Ping> builder)
    {
        builder.ToTable("pings", "tracking");
        builder.Property(p => p.Id).UseIdentityAlwaysColumn();
        builder.Property(p => p.Latitude).Coordinate();
        builder.Property(p => p.Longitude).Coordinate();
        builder.Check("leg", Mapping.In<RouteLeg>("leg"));
        builder.Check("place", Mapping.Place("latitude", "longitude"));
        builder.HasOne<Delivery>().WithMany().HasForeignKey(p => p.ShipmentId);
        // The path driven so far and the latest position, per shipment.
        builder.HasIndex(p => new { p.ShipmentId, p.Id });
    }
}
