using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;

namespace MyWorkplace.Customers.Persistence;

/// <summary>
/// EN: Lets <c>dotnet ef</c> create the context without starting the app; the connection string is never used to connect.
/// TR: <c>dotnet ef</c>'in uygulamayı başlatmadan context oluşturmasını sağlar; bağlantı cümlesiyle hiç bağlanılmaz.
/// </summary>
internal sealed class CustomersDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CustomersDbContext>
{
    /// <inheritdoc />
    public CustomersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CustomersDbContext>();
        options.UseServiceConventions("Host=localhost;Database=customers_design_time");
        return new CustomersDbContext(options.Options, new AnonymousCurrentUser());
    }
}
