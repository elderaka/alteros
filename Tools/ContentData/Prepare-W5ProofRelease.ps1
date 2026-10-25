param(
    [Parameter(Mandatory = $true)]
    [string]$ExportPath,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$ExportPath = (Resolve-Path -LiteralPath $ExportPath -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath (Join-Path $ExportPath 'manifest.json') -PathType Leaf)) {
    throw 'ExportPath must identify one explicit AuthoringJsonExporter snapshot directory containing manifest.json.'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('alteros-w5-proof-' + [guid]::NewGuid().ToString('N'))
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $OutputRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputRoot must stay under the system temporary directory.'
}
if (Test-Path -LiteralPath $OutputRoot) { throw 'OutputRoot already exists; choose a new path to preserve earlier artifacts.' }

& (Join-Path $PSScriptRoot 'Check-ContentData.ps1') -Group PrepareW5 -ManifestPath $ExportPath -OutputPath $OutputRoot
if ($LASTEXITCODE -ne 0) { throw "W5 proof preparation failed. Preserve and inspect $OutputRoot before retrying." }
