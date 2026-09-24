using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;

namespace MyWorkplace.Inventory.Persistence;

/// <summary>
/// EN: Lets <c>dotnet ef</c> create the context without starting the app; the connection string is never used to connect.
/// TR: <c>dotnet ef</c>'in uygulamayı başlatmadan context oluşturmasını sağlar; bağlantı cümlesiyle hiç bağlanılmaz.
/// </summary>
internal sealed class InventoryDbContextDesignTimeFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    /// <inheritdoc />
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>();
        options.UseServiceConventions("Host=localhost;Database=inventory_design_time");
        return new InventoryDbContext(options.Options, new AnonymousCurrentUser());
    }
}
