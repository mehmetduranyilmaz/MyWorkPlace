using System.Globalization;
using System.Text.Json;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Small readers for JSON bodies that several tests need (T-047).
/// TR: Birkaç testin ihtiyaç duyduğu, JSON gövdeleri için küçük okuyucular (T-047).
/// </summary>
internal static class Json
{
    /// <summary>
    /// EN: Reads a property whatever its letter case (an event's wire format is the messaging library's choice).
    /// TR: Bir özelliği harf büyüklüğünden bağımsız okur (bir olayın kablo biçimi mesajlaşma kütüphanesinin tercihidir).
    /// </summary>
    /// <param name="element">EN: JSON object. TR: JSON nesnesi.</param>
    /// <param name="name">EN: Property name. TR: Özellik adı.</param>
    /// <returns>EN: The value, or null. TR: Değer veya null.</returns>
    public static JsonElement? Property(JsonElement element, string name) =>
        element.EnumerateObject()
            .Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(p => (JsonElement?)p.Value)
            .FirstOrDefault();

    /// <summary>
    /// EN: A decimal as invariant text without trailing zeros: a numeric column returns 7.000 or 24.000000.
    /// TR: Sondaki sıfırlar olmadan, kültürden bağımsız metin olarak bir ondalık: numeric bir sütun 7.000 veya 24.000000 döner.
    /// </summary>
    /// <param name="value">EN: The value. TR: Değer.</param>
    /// <returns>EN: The text. TR: Metin.</returns>
    public static string Trim(decimal value) =>
        (value / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
}
