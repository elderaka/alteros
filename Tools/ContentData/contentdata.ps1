param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CliArguments
)

$ErrorActionPreference = 'Stop'
if ($null -eq $CliArguments -or $CliArguments.Count -lt 5 -or $CliArguments[0] -ne 'bake') {
    throw 'Usage: pwsh -File Tools/ContentData/contentdata.ps1 bake --manifest <trusted-manifest.json> --output <staging-content.sqlite3>'
}

$manifestPath = $null
$outputPath = $null
for ($index = 1; $index -lt $CliArguments.Count; $index += 2) {
    if ($index + 1 -ge $CliArguments.Count) { throw 'Every bake option requires a value.' }
    switch ($CliArguments[$index]) {
        '--manifest' { if ($manifestPath) { throw 'The bake manifest option may be supplied once.' }; $manifestPath = $CliArguments[$index + 1] }
        '--output' { if ($outputPath) { throw 'The bake output option may be supplied once.' }; $outputPath = $CliArguments[$index + 1] }
        default { throw "Unsupported content-data option: $($CliArguments[$index])" }
    }
}
if ([string]::IsNullOrWhiteSpace($manifestPath) -or [string]::IsNullOrWhiteSpace($outputPath)) {
    throw 'Both --manifest and --output are required.'
}
$manifestPath = (Resolve-Path -LiteralPath $manifestPath -ErrorAction Stop).Path
$outputPath = [System.IO.Path]::GetFullPath($outputPath)
$runner = Join-Path $PSScriptRoot 'Check-ContentData.ps1'
& $runner -Group BakeCommand -ManifestPath $manifestPath -OutputPath $outputPath
exit $LASTEXITCODE
