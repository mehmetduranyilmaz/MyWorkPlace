using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;

namespace MyWorkplace.Identity.Persistence;

/// <summary>
/// EN: Lets <c>dotnet ef</c> create the context without starting the app. The connection string is never used to
///     connect when adding migrations; it only selects the PostgreSQL provider.
/// TR: <c>dotnet ef</c>'in uygulamayı başlatmadan context oluşturmasını sağlar. Migration eklerken bağlantı cümlesiyle
///     hiç bağlanılmaz; sadece PostgreSQL sağlayıcısını seçmeye yarar.
/// </summary>
internal sealed class IdentityDbContextDesignTimeFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    /// <inheritdoc />
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        options.UseServiceConventions("Host=localhost;Database=identity_design_time");
        return new IdentityDbContext(options.Options, new AnonymousCurrentUser());
    }
}
