using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipment.Domain.Common;

namespace Shipment.Persistence.Configurations;

internal static class Mapping
{
    /// <summary>
    /// The audit columns (defaults for rows inserted by SQL - the demo seed) and xmin as the row version.
    /// </summary>
    public static void Audited<T>(this EntityTypeBuilder<T> builder)
        where T : Audited
    {
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(e => e.ModifiedAt).HasDefaultValueSql("now()");
        builder.Property(e => e.Version).IsRowVersion();
    }

    /// <summary>
    /// The database guards the values too: a CHECK named ck_&lt;table&gt;_&lt;name&gt;.
    /// </summary>
    public static void Check<T>(this EntityTypeBuilder<T> builder, string name, string sql)
        where T : class =>
        builder.ToTable(table =>
            table.HasCheckConstraint($"ck_{builder.Metadata.GetTableName()}_{name}", sql)
        );

    /// <summary>"column IN ('a', 'b')" over the stored names of an enum.</summary>
    public static string In<TEnum>(string column)
        where TEnum : struct, Enum =>
        $"{column} IN ({string.Join(", ", Enum.GetValues<TEnum>().Select(v => $"'{SnakeCaseEnumConverter<TEnum>.ToName(v)}'"))})";

    public const string Sizes = "('S', 'M', 'L', 'XL')";

    public static string Place(string latitude, string longitude) =>
        $"{latitude} BETWEEN -90 AND 90 AND {longitude} BETWEEN -180 AND 180";

    /// <summary>Latitude or longitude: numeric(9,6), no PostGIS on the shared server.</summary>
    public static PropertyBuilder<decimal> Coordinate(this PropertyBuilder<decimal> property) =>
        property.HasPrecision(9, 6);

    public static PropertyBuilder<decimal?> Coordinate(this PropertyBuilder<decimal?> property) =>
        property.HasPrecision(9, 6);
}
