namespace MyWorkplace;

/// <summary>
/// EN: The single source of the RabbitMQ version, for the same reason as <see cref="PostgresImage"/>: linked into the
///     AppHost and every test project that starts RabbitMQ, so development and tests always run the same broker.
///     To upgrade RabbitMQ, change <see cref="Tag"/> here and nowhere else.
/// TR: RabbitMQ sürümünün tek kaynağı; <see cref="PostgresImage"/> ile aynı nedenle: AppHost'a ve RabbitMQ başlatan her test
///     projesine bağlantı olarak eklenir; böylece geliştirme ve testler her zaman aynı mesaj aracını çalıştırır.
///     RabbitMQ'yu yükseltmek için sadece buradaki <see cref="Tag"/> değişir.
/// </summary>
internal static class RabbitMqImage
{
    /// <summary>EN: Docker Hub image name. TR: Docker Hub imaj adı.</summary>
    public const string Name = "rabbitmq";

    /// <summary>EN: Pinned image tag (broker version). TR: Sabitlenmiş imaj etiketi (mesaj aracı sürümü).</summary>
    public const string Tag = "4.3.6";

    /// <summary>EN: Full reference, e.g. "rabbitmq:4.3.6". TR: Tam referans, ör. "rabbitmq:4.3.6".</summary>
    public const string Reference = Name + ":" + Tag;
}
