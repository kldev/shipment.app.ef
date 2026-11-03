using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipment.Domain.Catalog;
using Shipment.Domain.Crm;

namespace Shipment.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", "catalog");
        builder.Audited();
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Sku).HasMaxLength(40);
        builder.Property(p => p.Name).HasMaxLength(200);
        builder.Property(p => p.Category).HasMaxLength(40);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.ListedAvailable).HasDefaultValue(true).HasSentinel(true);
        builder.Check("price", $"price_minor BETWEEN 1 AND {Product.MaxPriceMinor}");
        builder.Check("weight", $"weight_g BETWEEN 1 AND {Product.MaxWeightG}");
        builder.Check("size_class", $"size_class IN {Mapping.Sizes}");
        builder.Check("sku", "sku ~ '^[A-Z0-9][A-Z0-9-]{1,39}$'");
        builder.Check("category", "category ~ '^[a-z][a-z_]{1,39}$'");
        builder.HasOne<Partner>().WithMany().HasForeignKey(p => p.PartnerId);
        builder.HasIndex(p => new { p.PartnerId, p.Sku }).IsUnique();
        builder.HasIndex(p => new { p.PartnerId, p.Category });
    }
}
