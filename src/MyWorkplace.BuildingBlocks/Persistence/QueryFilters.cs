namespace MyWorkplace.BuildingBlocks.Persistence;

/// <summary>
/// EN: Names of the global query filters, so a single filter can be disabled deliberately
///     (e.g. <c>IgnoreQueryFilters([QueryFilters.SoftDelete])</c> to see deleted rows of the current tenant).
/// TR: Global sorgu filtrelerinin isimleri; böylece tek bir filtre bilinçli olarak kapatılabilir
///     (ör. aktif firmanın silinmiş kayıtlarını görmek için <c>IgnoreQueryFilters([QueryFilters.SoftDelete])</c>).
/// </summary>
public static class QueryFilters
{
    /// <summary>
    /// EN: Restricts <c>ITenantOwned</c> entities to the current tenant.
    /// TR: <c>ITenantOwned</c> entity'leri aktif firmayla sınırlar.
    /// </summary>
    public const string Tenant = "Tenant";

    /// <summary>
    /// EN: Hides deleted <c>ISoftDeletable</c> entities.
    /// TR: Silinmiş <c>ISoftDeletable</c> entity'leri gizler.
    /// </summary>
    public const string SoftDelete = "SoftDelete";
}
