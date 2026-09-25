using Microsoft.EntityFrameworkCore;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: The current company's unit catalog: system units (code) plus its own units (database), ADR-019. The one place
///     that answers "does this unit exist?" and "is it in use?", for the unit and item endpoints and for T-030.
/// TR: Aktif firmanın birim kataloğu: sistem birimleri (kod) artı kendi birimleri (veritabanı), ADR-019. "Bu birim var mı?" ve
///     "kullanımda mı?" sorularını birim ve kalem uç noktaları ile T-030 için cevaplayan tek yer.
/// </summary>
/// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
public sealed class UnitCatalog(InventoryDbContext db)
{
    /// <summary>
    /// EN: All units: system units first, in their display order, then the company's own by code.
    /// TR: Tüm birimler: önce görüntüleme sırasıyla sistem birimleri, sonra koda göre firmanın kendi birimleri.
    /// </summary>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The catalog. TR: Katalog.</returns>
    public async Task<IReadOnlyList<UnitDefinition>> ListAsync(CancellationToken cancellationToken)
    {
        var own = await db.Units
            .OrderBy(u => u.Code)
            .Select(u => new UnitDefinition(u.Code, u.Name, u.Precision, false))
            .ToListAsync(cancellationToken);

        return [.. SystemUnits.All, .. own];
    }

    /// <summary>
    /// EN: The unit with this code, or null.
    /// TR: Bu koddaki birim veya null.
    /// </summary>
    /// <param name="code">EN: Code, any case. TR: Kod, herhangi bir harf büyüklüğünde.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The unit or null. TR: Birim veya null.</returns>
    public async Task<UnitDefinition?> FindAsync(string code, CancellationToken cancellationToken)
    {
        if (SystemUnits.Find(code) is { } system)
        {
            return system;
        }

        var normalized = UnitOfMeasure.NormalizeCode(code);
        return await db.Units
            .Where(u => u.Code == normalized)
            .Select(u => new UnitDefinition(u.Code, u.Name, u.Precision, false))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// EN: Which of the given codes are not in the catalog.
    /// TR: Verilen kodlardan hangilerinin katalogda olmadığı.
    /// </summary>
    /// <param name="codes">EN: Codes, any case. TR: Kodlar, herhangi bir harf büyüklüğünde.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The unknown codes, normalized. TR: Normalleştirilmiş bilinmeyen kodlar.</returns>
    public async Task<IReadOnlySet<string>> FindUnknownAsync(IEnumerable<string> codes, CancellationToken cancellationToken)
    {
        var candidates = codes
            .Select(UnitOfMeasure.NormalizeCode)
            .Where(code => SystemUnits.Find(code) is null)
            .ToHashSet(StringComparer.Ordinal);
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var known = await db.Units.Where(u => candidates.Contains(u.Code)).Select(u => u.Code).ToListAsync(cancellationToken);
        candidates.ExceptWith(known);
        return candidates;
    }

    /// <summary>
    /// EN: Whether any live item uses the unit as its base unit or as an alternative unit.
    /// TR: Herhangi bir canlı kalemin birimi temel birim veya alternatif birim olarak kullanıp kullanmadığı.
    /// </summary>
    /// <param name="code">EN: Normalized code. TR: Normalleştirilmiş kod.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: True if in use. TR: Kullanımdaysa true.</returns>
    public Task<bool> IsInUseAsync(string code, CancellationToken cancellationToken) =>
        db.StockItems.AnyAsync(i => i.BaseUnit == code || i.Units.Any(u => u.UnitCode == code), cancellationToken);
}
