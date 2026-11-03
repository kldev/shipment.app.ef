using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipment.Domain.Messaging;

namespace Shipment.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", "messaging");
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.MessageType).HasMaxLength(100);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.Property(m => m.LastError).HasMaxLength(2000);
        builder.Check("type", "message_type ~ '^[a-z][a-z0-9-]*$'");
        // The relay's queue: pending in order; and the retention's sweep of processed ones.
        builder
            .HasIndex(m => new { m.OccurredAt, m.Id }, "ix_outbox_pending")
            .HasFilter("processed_at IS NULL");
        builder
            .HasIndex(m => m.ProcessedAt, "ix_outbox_processed")
            .HasFilter("processed_at IS NOT NULL");
    }
}
