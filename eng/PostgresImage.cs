namespace MyWorkplace;

/// <summary>
/// EN: The single source of the PostgreSQL version. Linked (not referenced) into the AppHost and every test project
///     that starts PostgreSQL, so development and tests always run the same server — and an Aspire update can't
///     change it silently. To upgrade PostgreSQL, change <see cref="Tag"/> here and nowhere else.
/// TR: PostgreSQL sürümünün tek kaynağı. AppHost'a ve PostgreSQL başlatan her test projesine (referans olarak değil)
///     bağlantı olarak eklenir; böylece geliştirme ve testler her zaman aynı sunucuyu çalıştırır — ve bir Aspire
///     güncellemesi bunu sessizce değiştiremez. PostgreSQL'i yükseltmek için sadece buradaki <see cref="Tag"/> değişir.
/// </summary>
internal static class PostgresImage
{
    /// <summary>EN: Docker Hub image name. TR: Docker Hub imaj adı.</summary>
    public const string Name = "postgres";

    /// <summary>EN: Pinned image tag (server version). TR: Sabitlenmiş imaj etiketi (sunucu sürümü).</summary>
    public const string Tag = "18.3";

    /// <summary>EN: Full reference, e.g. "postgres:18.3". TR: Tam referans, ör. "postgres:18.3".</summary>
    public const string Reference = Name + ":" + Tag;
}
