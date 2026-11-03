using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipment.Domain.Billing;
using Shipment.Domain.Carriers;
using Shipment.Domain.Ordering;

namespace Shipment.Persistence.Configurations;

internal sealed class PayoutRateConfiguration : IEntityTypeConfiguration<PayoutRate>
{
    public void Configure(EntityTypeBuilder<PayoutRate> builder)
    {
        builder.ToTable("payout_rates", "billing");
        builder.HasKey(r => r.VehicleType);
        builder.Property(r => r.VehicleType).HasMaxLength(10);
        builder.HasOne<VehicleType>().WithMany().HasForeignKey(r => r.VehicleType);
        builder.HasData(PayoutRate.All);
    }
}

internal sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("processed_events", "billing");
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.MessageType).HasMaxLength(50);
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("entries", "billing");
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();
        builder.Property(e => e.OrderCode).HasMaxLength(12);
        builder.Property(e => e.Description).HasMaxLength(200);
        builder.Check("kind", Mapping.In<EntryKind>("kind"));
        builder.Check("amount", "amount_minor >= 0");
        builder.Check(
            "line",
            "(line_no IS NULL) = (product_id IS NULL) AND (line_no IS NULL) = (quantity IS NULL) AND (line_no IS NULL OR kind = 'refund')"
        );
        builder.Check(
            "delivery",
            "kind NOT IN ('commission', 'payout', 'tip') OR shipment_id IS NOT NULL"
        );
        builder.Check("courier", "(kind IN ('payout', 'tip')) = (courier_id IS NOT NULL)");
        builder.HasOne<ProcessedEvent>().WithMany().HasForeignKey(e => e.EventId);
        builder.HasOne<Order>().WithMany().HasForeignKey(e => e.OrderId);

        // One entry of a kind per order; refunds one per missing line, or one for the whole order. They make the
        // reactions idempotent even when another event books the same thing.
        builder
            .HasIndex(e => new { e.OrderId, e.Kind }, "ux_entries_order_kind")
            .IsUnique()
            .HasFilter("kind <> 'refund'");
        builder
            .HasIndex(e => new { e.OrderId, e.LineNo }, "ux_entries_refund_line")
            .IsUnique()
            .HasFilter("kind = 'refund' AND line_no IS NOT NULL");
        builder
            .HasIndex(e => e.OrderId, "ux_entries_refund_all")
            .IsUnique()
            .HasFilter("kind = 'refund' AND line_no IS NULL");
        builder.HasIndex(e => e.OrderId);
        builder.HasIndex(e => e.EventId);
        builder.HasIndex(e => e.BookedAt);
        builder.HasIndex(e => new { e.PartnerId, e.BookedAt });
        builder.HasIndex(e => new { e.CourierId, e.BookedAt }).HasFilter("courier_id IS NOT NULL");
    }
}
