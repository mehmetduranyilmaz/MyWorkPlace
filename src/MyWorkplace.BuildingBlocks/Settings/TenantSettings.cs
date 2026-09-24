using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Persistence;

namespace MyWorkplace.BuildingBlocks.Settings;

/// <summary>
/// EN: Reads and saves the current company's settings of one module (ADR-018).
/// TR: Aktif firmanın bir modüle ait ayarlarını okur ve kaydeder (ADR-018).
/// </summary>
/// <typeparam name="TSettings">EN: The module's settings class. TR: Modülün ayar sınıfı.</typeparam>
public interface ITenantSettings<TSettings>
    where TSettings : class, IModuleSettings, new()
{
    /// <summary>
    /// EN: The current values; the code defaults when the company never saved any.
    /// TR: Güncel değerler; firma hiç kaydetmediyse koddaki varsayılanlar.
    /// </summary>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The settings. TR: Ayarlar.</returns>
    Task<TSettings> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// EN: The current values with their version for the ETag; version 0 means "never saved".
    /// TR: ETag için sürümüyle birlikte güncel değerler; 0 sürümü "hiç kaydedilmedi" demektir.
    /// </summary>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: Settings and version. TR: Ayarlar ve sürüm.</returns>
    Task<Versioned<TSettings>> GetWithVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// EN: Replaces all values if the stored version is still <paramref name="expectedVersion"/> (0 for a first save).
    /// TR: Saklanan sürüm hâlâ <paramref name="expectedVersion"/> ise tüm değerleri değiştirir (ilk kayıt için 0).
    /// </summary>
    /// <param name="values">EN: New values. TR: Yeni değerler.</param>
    /// <param name="expectedVersion">EN: Version the caller read. TR: Çağıranın okuduğu sürüm.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The new version, or null if someone saved in between. TR: Yeni sürüm; arada biri kaydettiyse null.</returns>
    Task<uint?> SaveAsync(TSettings values, uint expectedVersion, CancellationToken cancellationToken = default);
}

/// <summary>
/// EN: The <c>tenant_settings</c> implementation. The tenant filter of <see cref="TenantSetting"/> keeps companies apart.
/// TR: <c>tenant_settings</c> uygulaması. <see cref="TenantSetting"/>'in firma filtresi firmaları birbirinden ayırır.
/// </summary>
/// <typeparam name="TSettings">EN: The module's settings class. TR: Modülün ayar sınıfı.</typeparam>
/// <param name="db">EN: The service's database. TR: Servisin veritabanı.</param>
public sealed class TenantSettings<TSettings>(ServiceDbContext db) : ITenantSettings<TSettings>
    where TSettings : class, IModuleSettings, new()
{
    /// <summary>
    /// EN: The module key, copied once: query expressions can't read a static abstract member directly.
    /// TR: Modül anahtarı, bir kez kopyalanır: sorgu ifadeleri statik soyut bir üyeyi doğrudan okuyamaz.
    /// </summary>
    private static readonly string _module = TSettings.Module;

    /// <inheritdoc />
    public async Task<TSettings> GetAsync(CancellationToken cancellationToken = default) =>
        (await GetWithVersionAsync(cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<Versioned<TSettings>> GetWithVersionAsync(CancellationToken cancellationToken = default)
    {
        var row = await db.TenantSettings
            .Where(s => s.Module == _module)
            .Select(s => new { s.Values, Version = EF.Property<uint>(s, ServiceDbContext.ConcurrencyTokenProperty) })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? new(new TSettings(), 0) : new(SettingsJson.Deserialize<TSettings>(row.Values), row.Version);
    }

    /// <inheritdoc />
    public async Task<uint?> SaveAsync(TSettings values, uint expectedVersion, CancellationToken cancellationToken = default)
    {
        var json = SettingsJson.Serialize(values);
        var row = await db.TenantSettings.AsTracking()
            .SingleOrDefaultAsync(s => s.Module == _module, cancellationToken);

        if (row is null)
        {
            return expectedVersion == 0 ? await InsertAsync(json, cancellationToken) : null;
        }

        if (db.GetVersion(row) != expectedVersion)
        {
            return null;
        }

        // EN: PostgreSQL rewrites jsonb in its own format, so compare meaning, not text; saving the same values must not
        //     create a change-history entry.
        // TR: PostgreSQL jsonb'yi kendi biçiminde yeniden yazar; bu yüzden metin değil anlam karşılaştırılır; aynı değerleri
        //     kaydetmek değişiklik geçmişine kayıt düşürmemelidir.
        if (SettingsJson.Serialize(SettingsJson.Deserialize<TSettings>(row.Values)) == json)
        {
            return expectedVersion;
        }

        db.ExpectVersion(row, expectedVersion);
        row.Values = json;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }

        return db.GetVersion(row);
    }

    /// <summary>
    /// EN: First save of the company. Two first saves racing each other hit the unique (tenant, module) index; the loser
    ///     gets a conflict, exactly as with a stale version.
    /// TR: Firmanın ilk kaydı. Yarışan iki ilk kayıt benzersiz (firma, modül) indeksine takılır; kaybeden, eskimiş sürümdeki
    ///     gibi çakışma alır.
    /// </summary>
    /// <param name="json">EN: Serialized values. TR: Serileştirilmiş değerler.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The new version, or null on a conflict. TR: Yeni sürüm; çakışmada null.</returns>
    private async Task<uint?> InsertAsync(string json, CancellationToken cancellationToken)
    {
        var row = new TenantSetting { Module = TSettings.Module, Values = json };
        db.TenantSettings.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return null;
        }

        return db.GetVersion(row);
    }
}

/// <summary>
/// EN: The one JSON format of settings: camelCase, enums as names. Unknown stored fields are ignored and missing ones
///     keep their code defaults, so adding or removing a setting never breaks stored documents.
/// TR: Ayarların tek JSON biçimi: camelCase, enum'lar ad olarak. Saklanan bilinmeyen alanlar yok sayılır, eksik olanlar
///     koddaki varsayılanlarını korur; böylece ayar eklemek veya çıkarmak saklanan dokümanları asla bozmaz.
/// </summary>
internal static class SettingsJson
{
    /// <summary>EN: Serializer options. TR: Serileştirici seçenekleri.</summary>
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// EN: Serializes settings.
    /// TR: Ayarları serileştirir.
    /// </summary>
    /// <typeparam name="T">EN: Settings type. TR: Ayar tipi.</typeparam>
    /// <param name="value">EN: Settings. TR: Ayarlar.</param>
    /// <returns>EN: JSON text. TR: JSON metni.</returns>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, _options);

    /// <summary>
    /// EN: Deserializes settings; a null document gives the defaults.
    /// TR: Ayarları geri okur; null doküman varsayılanları verir.
    /// </summary>
    /// <typeparam name="T">EN: Settings type. TR: Ayar tipi.</typeparam>
    /// <param name="json">EN: JSON text. TR: JSON metni.</param>
    /// <returns>EN: Settings. TR: Ayarlar.</returns>
    public static T Deserialize<T>(string json)
        where T : new() =>
        JsonSerializer.Deserialize<T>(json, _options) ?? new T();
}
