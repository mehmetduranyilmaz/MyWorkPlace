using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyWorkplace.BuildingBlocks.Identity;

namespace MyWorkplace.BuildingBlocks.Persistence;

/// <summary>
/// EN: Lets <c>dotnet ef</c> create a service's context without starting the app. <c>dotnet ef</c> only looks for a
///     factory in the service's own assembly, so each service declares a one-line subclass:
///     <c>internal sealed class DesignTimeFactory : ServiceDbContextDesignTimeFactory&lt;CustomersDbContext&gt;;</c>
///     The connection string only selects the provider; it is never used to connect.
/// TR: <c>dotnet ef</c>'in bir servisin context'ini uygulamayı başlatmadan oluşturmasını sağlar. <c>dotnet ef</c> fabrikayı sadece
///     servisin kendi assembly'sinde aradığı için her servis tek satırlık bir alt sınıf tanımlar:
///     <c>internal sealed class DesignTimeFactory : ServiceDbContextDesignTimeFactory&lt;CustomersDbContext&gt;;</c>
///     Bağlantı cümlesi sadece sağlayıcıyı seçer; onunla hiç bağlanılmaz.
/// </summary>
/// <typeparam name="TContext">
/// EN: The service's context; must take (options, ICurrentUser). TR: Servisin context'i; (options, ICurrentUser) almalıdır.
/// </typeparam>
public abstract class ServiceDbContextDesignTimeFactory<TContext> : IDesignTimeDbContextFactory<TContext>
    where TContext : ServiceDbContext
{
    /// <inheritdoc />
    public TContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TContext>();
        options.UseServiceConventions($"Host=localhost;Database={typeof(TContext).Name}_design_time");
        return (TContext)Activator.CreateInstance(typeof(TContext), options.Options, new AnonymousCurrentUser())!;
    }
}
