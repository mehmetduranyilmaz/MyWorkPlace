# EN: Turns failed tests in TRX files into GitHub Actions annotations and a job summary, so a red CI run names its
#     failing tests where anyone can read them — annotations are public, raw logs need a signed-in user (T-043).
# TR: TRX dosyalarındaki başarısız testleri GitHub Actions uyarılarına (annotation) ve bir iş özetine çevirir; böylece kırmızı bir
#     CI çalıştırması başarısız testlerini herkesin okuyabileceği yerde söyler — uyarılar herkese açıktır, ham loglar giriş ister (T-043).

param(
    # EN: Folder with the TRX files (searched recursively). TR: TRX dosyalarının klasörü (alt klasörlerle birlikte aranır).
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory
)

# EN: Annotation text must escape %, CR and LF; property values (the title) also ':' and ','.
# TR: Uyarı metninde %, CR ve LF kaçışlanmalıdır; özellik değerlerinde (başlık) ':' ve ',' de.
function Format-Data([string] $text) {
    return $text.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
}

function Format-Property([string] $text) {
    return (Format-Data $text).Replace(':', '%3A').Replace(',', '%2C')
}

$namespace = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }
$failed = @()

foreach ($file in Get-ChildItem -Path $ResultsDirectory -Filter '*.trx' -Recurse) {
    [xml] $trx = Get-Content -Raw -LiteralPath $file.FullName
    foreach ($match in Select-Xml -Xml $trx -XPath '//t:UnitTestResult[@outcome="Failed"]' -Namespace $namespace) {
        $result = $match.Node
        $message = "$($result.Output.ErrorInfo.Message)".Trim()
        $stack = "$($result.Output.ErrorInfo.StackTrace)".Trim()
        $failed += [pscustomobject]@{ Name = $result.testName; Message = $message }

        # EN: Long stack traces are cut: an annotation is a pointer, the artifact has everything.
        # TR: Uzun yığın izleri kesilir: uyarı bir işarettir, her şey artifact'tadır.
        $body = "$message`n$stack"
        if ($body.Length -gt 3000) {
            $body = $body.Substring(0, 3000) + "`n..."
        }

        Write-Output "::error title=$(Format-Property $result.testName)::$(Format-Data $body)"
    }
}

if ($env:GITHUB_STEP_SUMMARY) {
    $summary = @("## Failed tests ($($failed.Count))", '', '| Test | Message |', '| --- | --- |')
    foreach ($test in $failed) {
        $firstLine = ($test.Message -split "`n")[0].Replace('|', '\|')
        $summary += "| ``$($test.Name)`` | $firstLine |"
    }

    $summary | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

Write-Output "$($failed.Count) failed test(s) reported."
