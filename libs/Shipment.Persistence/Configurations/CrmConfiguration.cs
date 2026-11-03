using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipment.Domain.Crm;

namespace Shipment.Persistence.Configurations;

internal sealed class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.ToTable("partners", "crm");
        builder.Audited();
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Code).HasMaxLength(12);
        builder.Property(p => p.Name).HasMaxLength(200);
        // Defaults for rows inserted by SQL (the demo data); the entity always sets them.
        builder.Property(p => p.Status).HasDefaultValue(PartnerStatus.Active);
        builder.Check("kind", Mapping.In<PartnerKind>("kind"));
        builder.Check("status", Mapping.In<PartnerStatus>("status"));
        builder.Check("code", "code ~ '^[A-Z0-9]{2,12}$'");
        builder.Check("commission", $"commission_bps BETWEEN 0 AND {Partner.MaxCommissionBps}");
        builder.HasIndex(p => p.Code).IsUnique();
    }
}

internal sealed class PickupPointConfiguration : IEntityTypeConfiguration<PickupPoint>
{
    public void Configure(EntityTypeBuilder<PickupPoint> builder)
    {
        builder.ToTable("pickup_points", "crm");
        builder.Audited();
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(200);
        builder.Property(p => p.AddressLine).HasMaxLength(200);
        builder.Property(p => p.City).HasMaxLength(100);
        builder.Property(p => p.Postcode).HasMaxLength(10);
        builder.Property(p => p.Latitude).Coordinate();
        builder.Property(p => p.Longitude).Coordinate();
        builder.Property(p => p.OpensMinute).HasColumnType("smallint");
        builder.Property(p => p.ClosesMinute).HasColumnType("smallint");
        builder.Property(p => p.Status).HasDefaultValue(PickupPointStatus.Active);
        builder.Check("status", Mapping.In<PickupPointStatus>("status"));
        // Closing after midnight is not modelled: opens before closes on the same day.
        builder.Check(
            "hours",
            "opens_minute >= 0 AND closes_minute <= 1440 AND opens_minute < closes_minute"
        );
        builder.Check("place", Mapping.Place("latitude", "longitude"));
        builder.HasOne(p => p.Partner).WithMany().HasForeignKey(p => p.PartnerId);
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers", "crm");
        builder.Audited();
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.DisplayName).HasMaxLength(200);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.Phone).HasMaxLength(30);
        builder.Check("email", "email = lower(btrim(email)) AND email LIKE '%_@_%'");
        builder.HasIndex(c => c.Email).IsUnique();
        builder.HasMany(c => c.Addresses).WithOne().HasForeignKey(a => a.CustomerId);
        builder.Navigation(c => c.Addresses).HasField("_addresses");
    }
}

internal sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("customer_addresses", "crm");
        builder.Audited();
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Label).HasMaxLength(40);
        builder.Property(a => a.AddressLine).HasMaxLength(200);
        builder.Property(a => a.City).HasMaxLength(100);
        builder.Property(a => a.Postcode).HasMaxLength(10);
        builder.Property(a => a.Latitude).Coordinate();
        builder.Property(a => a.Longitude).Coordinate();
        builder.Property(a => a.Notes).HasMaxLength(500);
        builder.Check("place", Mapping.Place("latitude", "longitude"));
        // One default per customer is the Customer aggregate's rule (its row version guards it); a partial unique index
        // would make EF order the flag's hand-over wrongly within one save.
    }
}
