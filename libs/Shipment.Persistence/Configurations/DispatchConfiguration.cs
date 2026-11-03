using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipment.Domain.Carriers;
using Shipment.Domain.Crm;
using Shipment.Domain.Dispatch;
using Shipment.Domain.Ordering;

namespace Shipment.Persistence.Configurations;

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("shipments", "dispatch");
        builder.Audited();
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Code).HasMaxLength(12);
        builder.Property(s => s.OrderCode).HasMaxLength(12);
        builder.Property(s => s.CustomerName).HasMaxLength(200);
        builder.Property(s => s.PartnerName).HasMaxLength(200);
        builder.Property(s => s.PickupPointName).HasMaxLength(200);
        builder.Property(s => s.PickupAddressLine).HasMaxLength(200);
        builder.Property(s => s.PickupLatitude).Coordinate();
        builder.Property(s => s.PickupLongitude).Coordinate();
        builder.Property(s => s.DropoffLabel).HasMaxLength(40);
        builder.Property(s => s.DropoffAddressLine).HasMaxLength(200);
        builder.Property(s => s.DropoffCity).HasMaxLength(100);
        builder.Property(s => s.DropoffNotes).HasMaxLength(500);
        builder.Property(s => s.DropoffLatitude).Coordinate();
        builder.Property(s => s.DropoffLongitude).Coordinate();
        builder.Property(s => s.RequiredVehicle).HasMaxLength(10);
        builder.Property(s => s.FailureReason).HasMaxLength(500);
        builder.Ignore(s => s.IsActive);
        builder.Check("status", Mapping.In<ShipmentStatus>("status"));
        // A courier from courier_assigned on; a failed shipment keeps who failed it and why.
        builder.Check("courier", "(status = 'available') = (courier_id IS NULL)");
        builder.Check("failure", "(status = 'failed') = (failure_reason IS NOT NULL)");
        builder.Check("size", $"largest_size_class IN {Mapping.Sizes}");
        builder.Check("freight", "item_count > 0 AND weight_g >= 0 AND distance_m >= 0");
        builder.HasOne<Order>().WithOne().HasForeignKey<Delivery>(s => s.OrderId);
        builder.HasOne<PickupPoint>().WithMany().HasForeignKey(s => s.PickupPointId);
        builder.HasOne<Courier>().WithMany().HasForeignKey(s => s.CourierId);
        builder.HasOne<VehicleType>().WithMany().HasForeignKey(s => s.RequiredVehicle);

        builder.HasIndex(s => s.Code).IsUnique();
        builder.HasIndex(s => s.OrderId).IsUnique();
        // The Shipments page, oldest waiting first; by status; a courier's shipments.
        builder.HasIndex(s => new { s.CreatedAt, s.Id });
        builder.HasIndex(s => new { s.Status, s.CreatedAt });
        builder.HasIndex(s => s.CourierId).HasFilter("courier_id IS NOT NULL");
        builder.HasIndex(s => s.PickupPointId);
    }
}
