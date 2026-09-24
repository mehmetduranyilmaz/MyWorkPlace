using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MyWorkplace.BuildingBlocks.Persistence;

/// <summary>
/// EN: Recognizes PostgreSQL errors that map to HTTP answers, so services don't repeat provider-specific checks.
/// TR: HTTP cevaplarına karşılık gelen PostgreSQL hatalarını tanır; böylece servisler sağlayıcıya özgü kontrolleri tekrarlamaz.
/// </summary>
public static class PostgresErrors
{
    /// <summary>
    /// EN: True when a save failed on a unique index — e.g. two requests racing to create the same email.
    /// TR: Kaydetme benzersiz bir indekse takıldıysa true — ör. aynı e-postayı oluşturmak için yarışan iki istek.
    /// </summary>
    /// <param name="exception">EN: The save error. TR: Kaydetme hatası.</param>
    /// <returns>EN: True for a unique violation. TR: Benzersizlik ihlalinde true.</returns>
    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
