using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Shipment.Persistence;

/// <summary>`dotnet ef migrations add` builds the model here; it never connects.</summary>
[UsedImplicitly]
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ShipmentDbContext>
{
    public ShipmentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShipmentDbContext>();
        options.UseShipmentNpgsql(NpgsqlDataSource.Create("Host=localhost;Database=shipment"));
        return new ShipmentDbContext(options.Options);
    }
}
