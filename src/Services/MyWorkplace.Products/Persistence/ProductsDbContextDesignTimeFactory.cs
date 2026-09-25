using MyWorkplace.BuildingBlocks.Persistence;

namespace MyWorkplace.Products.Persistence;

/// <summary>
/// EN: Lets <c>dotnet ef</c> create the context at design time (ADR-021).
/// TR: <c>dotnet ef</c>'in context'i tasarım zamanında oluşturmasını sağlar (ADR-021).
/// </summary>
internal sealed class ProductsDbContextDesignTimeFactory : ServiceDbContextDesignTimeFactory<ProductsDbContext>;
