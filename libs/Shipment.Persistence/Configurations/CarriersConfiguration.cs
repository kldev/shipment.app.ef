using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipment.Domain.Carriers;
using Shipment.Domain.Compliance;

namespace Shipment.Persistence.Configurations;

internal sealed class VehicleTypeConfiguration : IEntityTypeConfiguration<VehicleType>
{
    public void Configure(EntityTypeBuilder<VehicleType> builder)
    {
        builder.ToTable("vehicle_types", "carriers");
        builder.HasKey(v => v.Code);
        builder.Property(v => v.Code).HasMaxLength(10);
        builder.Property(v => v.Name).HasMaxLength(50);
        builder.Check("size", $"max_size_class IN {Mapping.Sizes}");
        builder.HasData(VehicleType.All);
    }
}

internal sealed class CourierConfiguration : IEntityTypeConfiguration<Courier>
{
    public void Configure(EntityTypeBuilder<Courier> builder)
    {
        builder.ToTable("couriers", "carriers");
        builder.Audited();
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.DisplayName).HasMaxLength(200);
        builder.Property(c => c.Phone).HasMaxLength(30);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.VehicleType).HasMaxLength(10);
        builder.Property(c => c.VehiclePlate).HasMaxLength(15);
        builder.Property(c => c.ShiftStatus).HasDefaultValue(ShiftStatus.Offline);
        builder.Property(c => c.LastLatitude).Coordinate();
        builder.Property(c => c.LastLongitude).Coordinate();
        builder.Check("shift_status", Mapping.In<ShiftStatus>("shift_status"));
        builder.Check("plate", $"vehicle_type = '{VehicleType.Bike}' OR vehicle_plate IS NOT NULL");
        builder.Check("shift_started", "(shift_status = 'offline') = (shift_started_at IS NULL)");
        builder.Check(
            "position",
            "(last_latitude IS NULL) = (last_longitude IS NULL) AND (last_latitude IS NULL) = (last_position_at IS NULL)"
        );
        builder.HasOne<VehicleType>().WithMany().HasForeignKey(c => c.VehicleType);
        builder.HasIndex(c => c.Email).IsUnique();
        // Who is on shift - the dispatch proposals.
        builder.HasIndex(c => c.ShiftStatus).HasFilter("shift_status <> 'offline'");
    }
}

internal sealed class CourierDocumentConfiguration : IEntityTypeConfiguration<CourierDocument>
{
    public void Configure(EntityTypeBuilder<CourierDocument> builder)
    {
        builder.ToTable("courier_documents", "compliance");
        builder.Audited();
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Number).HasMaxLength(50);
        builder.Check("kind", Mapping.In<DocumentKind>("kind"));
        builder.Check("verified", "(verified_at IS NULL) = (verified_by IS NULL)");
        builder.HasOne<Courier>().WithMany().HasForeignKey(d => d.CourierId);
        builder.HasIndex(d => new { d.CourierId, d.Kind }).IsUnique();
    }
}
