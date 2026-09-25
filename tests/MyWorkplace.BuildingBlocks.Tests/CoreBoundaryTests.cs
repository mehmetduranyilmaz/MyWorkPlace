using System.Text.RegularExpressions;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: Guards the core boundary of ADR-026: the reusable projects contain no term of this product — no plan names, no
///     token issuer or audience, no Identity address, no reference to the product's contracts. A product value that
///     creeps back into the core fails here, not in the next project that reuses it.
/// TR: ADR-026'nın çekirdek sınırını korur: yeniden kullanılabilir projelerde bu ürüne ait hiçbir terim yoktur — plan adı, token
///     issuer'ı veya audience'ı, Identity adresi, ürünün sözleşmelerine referans yok. Çekirdeğe geri sızan bir ürün değeri, onu yeniden
///     kullanan bir sonraki projede değil, burada yakalanır.
/// </summary>
public sealed partial class CoreBoundaryTests
{
    /// <summary>EN: The core projects, relative to the repository root. TR: Depo köküne göre çekirdek projeler.</summary>
    private static readonly string[] _coreProjects =
    [
        "src/MyWorkplace.Abstractions",
        "src/MyWorkplace.BuildingBlocks",
        "src/MyWorkplace.ServiceDefaults",
    ];

    [Fact]
    public void CoreSources_ContainNoProductTerm()
    {
        var root = FindRepositoryRoot();
        var files = _coreProjects
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, project), "*.*", SearchOption.AllDirectories))
            .Where(IsSource)
            .ToList();

        var findings = files
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(entry => ProductTerm().IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(root, entry.file)}:{entry.number}: {entry.line.Trim()}")
            .ToList();

        Assert.NotEmpty(files);
        Assert.True(findings.Count == 0, "Product terms in the core:\n" + string.Join('\n', findings));
    }

    /// <summary>
    /// EN: This product's terms: plan names as literals, the "myworkplace-" token values, the Identity address and the
    ///     product's contracts project.
    /// TR: Bu ürünün terimleri: sabit metin olarak plan adları, "myworkplace-" token değerleri, Identity adresi ve ürünün sözleşme projesi.
    /// </summary>
    /// <returns>EN: The pattern. TR: Desen.</returns>
    [GeneratedRegex("""
        "(basic|pro)"|myworkplace-|https?(\+https?)?://identity|MyWorkplace\.Contracts
        """, RegexOptions.IgnoreCase)]
    private static partial Regex ProductTerm();

    /// <summary>
    /// EN: Source and project files, skipping build output.
    /// TR: Kaynak ve proje dosyaları; derleme çıktısı atlanır.
    /// </summary>
    /// <param name="path">EN: File path. TR: Dosya yolu.</param>
    /// <returns>EN: True for a file to check. TR: Kontrol edilecek dosyaysa true.</returns>
    private static bool IsSource(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Contains("bin") && !segments.Contains("obj")
            && Path.GetExtension(path) is ".cs" or ".csproj" or ".json";
    }

    /// <summary>
    /// EN: Walks up from the test output folder to the folder holding the solution file.
    /// TR: Test çıktı klasöründen çözüm dosyasını içeren klasöre kadar yukarı çıkar.
    /// </summary>
    /// <returns>EN: The repository root. TR: Depo kökü.</returns>
    private static string FindRepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "MyWorkplace.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("MyWorkplace.slnx not found above " + AppContext.BaseDirectory);
    }
}
