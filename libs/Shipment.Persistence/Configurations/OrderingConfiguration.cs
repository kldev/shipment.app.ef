using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipment.Domain.Catalog;
using Shipment.Domain.Crm;
using Shipment.Domain.Ordering;

namespace Shipment.Persistence.Configurations;

internal sealed class DeliveryFeeBandConfiguration : IEntityTypeConfiguration<DeliveryFeeBand>
{
    public void Configure(EntityTypeBuilder<DeliveryFeeBand> builder)
    {
        builder.ToTable("delivery_fee_bands", "ordering");
        builder.HasKey(b => b.MaxDistanceM);
        builder.Property(b => b.MaxDistanceM).ValueGeneratedNever();
        builder.HasData(DeliveryFeeBand.All);
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", "ordering");
        builder.Audited();
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Code).HasMaxLength(12);
        builder.Property(o => o.CustomerName).HasMaxLength(200);
        builder.Property(o => o.AddressLabel).HasMaxLength(40);
        builder.Property(o => o.AddressLine).HasMaxLength(200);
        builder.Property(o => o.AddressCity).HasMaxLength(100);
        builder.Property(o => o.AddressPostcode).HasMaxLength(10);
        builder.Property(o => o.AddressNotes).HasMaxLength(500);
        builder.Property(o => o.AddressLatitude).Coordinate();
        builder.Property(o => o.AddressLongitude).Coordinate();
        builder.Property(o => o.PartnerName).HasMaxLength(200);
        builder.Property(o => o.PickupPointName).HasMaxLength(200);
        builder.Property(o => o.PickupAddressLine).HasMaxLength(200);
        builder.Property(o => o.PickupLatitude).Coordinate();
        builder.Property(o => o.PickupLongitude).Coordinate();
        builder.Property(o => o.TipPercent).HasColumnType("smallint");
        builder.Property(o => o.CancelReason).HasMaxLength(500);
        builder.Property(o => o.FailureReason).HasMaxLength(500);
        builder.Check("status", Mapping.In<OrderStatus>("status"));
        builder.Check("tip", "tip_percent IN (0, 5, 10) AND tip_minor >= 0");
        builder.Check(
            "money",
            "delivery_fee_minor >= 0 AND goods_ordered_minor > 0 AND coalesce(goods_picked_minor, 0) >= 0"
        );
        builder.HasOne<Customer>().WithMany().HasForeignKey(o => o.CustomerId);
        builder.HasOne<Partner>().WithMany().HasForeignKey(o => o.PartnerId);
        builder.HasOne<PickupPoint>().WithMany().HasForeignKey(o => o.PickupPointId);
        builder.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId);
        builder.Navigation(o => o.Lines).HasField("_lines");

        builder.HasIndex(o => o.Code).IsUnique();
        // The Orders page (newest first), lists by status, the picking queue and history.
        builder.HasIndex(o => new { o.PlacedAt, o.Id }).IsDescending(true, false);
        builder.HasIndex(o => new { o.Status, o.PlacedAt }).IsDescending(false, true);
        builder
            .HasIndex(o => new { o.PlacedAt, o.Id }, "ix_orders_queue")
            .HasFilter("status IN ('placed', 'picking')");
        builder.HasIndex(o => o.CustomerId);
        builder.HasIndex(o => o.PartnerId);
        builder.HasIndex(o => o.PickupPointId);
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines", "ordering");
        builder.HasKey(l => new { l.OrderId, l.LineNo });
        builder.Property(l => l.LineNo).ValueGeneratedNever();
        builder.Property(l => l.Sku).HasMaxLength(40);
        builder.Property(l => l.Name).HasMaxLength(200);
        builder.Check("quantity", $"quantity BETWEEN 1 AND {OrderLine.MaxQuantity}");
        builder.Check(
            "picked",
            "picked_quantity IS NULL OR picked_quantity BETWEEN 0 AND quantity"
        );
        builder.Check("size", $"size_class IN {Mapping.Sizes}");
        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId);
        builder.HasIndex(l => new { l.OrderId, l.ProductId }).IsUnique();
        builder.HasIndex(l => l.ProductId);
    }
}
