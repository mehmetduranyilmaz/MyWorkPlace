namespace MyWorkplace.BuildingBlocks.Persistence;

/// <summary>
/// EN: Builds safe LIKE / ILIKE patterns from user input. <c>%</c> and <c>_</c> are wildcards in SQL; unescaped, a
///     user typing "%" would match everything and crafted patterns could make searches needlessly expensive.
///     Use with <see cref="EscapeCharacter"/>: <c>EF.Functions.ILike(column, pattern, SearchPattern.EscapeCharacter)</c>.
/// TR: Kullanıcı girdisinden güvenli LIKE / ILIKE kalıpları üretir. <c>%</c> ve <c>_</c> SQL'de joker karakterdir; kaçış
///     yapılmazsa "%" yazan kullanıcı her şeyle eşleşir ve özel kalıplar aramaları gereksiz yere pahalı yapabilir.
///     <see cref="EscapeCharacter"/> ile kullanılır: <c>EF.Functions.ILike(sütun, kalıp, SearchPattern.EscapeCharacter)</c>.
/// </summary>
public static class SearchPattern
{
    /// <summary>EN: Escape character used in the patterns. TR: Kalıplarda kullanılan kaçış karakteri.</summary>
    public const string EscapeCharacter = "\\";

    /// <summary>
    /// EN: Pattern matching values that contain <paramref name="text"/> literally.
    /// TR: <paramref name="text"/> metnini harfiyen içeren değerlerle eşleşen kalıp.
    /// </summary>
    /// <param name="text">EN: User's search text. TR: Kullanıcının arama metni.</param>
    /// <returns>EN: The escaped pattern, e.g. "%100\%%". TR: Kaçışlı kalıp, ör. "%100\%%".</returns>
    public static string Contains(string text) =>
        "%" + text
            .Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter, StringComparison.Ordinal)
            .Replace("%", EscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", EscapeCharacter + "_", StringComparison.Ordinal)
        + "%";
}
