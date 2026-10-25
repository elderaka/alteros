param(
    [Parameter(Mandatory = $true)][string]$BaselineExportPath,
    [Parameter(Mandatory = $true)][string]$PreparedSnapshotPath,
    [Parameter(Mandatory = $true)][string]$BackupPath,
    [Parameter(Mandatory = $true)][string]$ExpectedStoreId,
    [Parameter(Mandatory = $true)][long]$ExpectedStoreRevision,
    [Parameter(Mandatory = $true)][string]$ExpectedSelectionHash,
    [Parameter(Mandatory = $true)][string]$ExpectedSnapshotHash,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$BaselineExportPath = (Resolve-Path -LiteralPath $BaselineExportPath -ErrorAction Stop).Path
$PreparedSnapshotPath = (Resolve-Path -LiteralPath $PreparedSnapshotPath -ErrorAction Stop).Path
$BackupPath = (Resolve-Path -LiteralPath $BackupPath -ErrorAction Stop).Path
foreach ($rootPath in @($BaselineExportPath, $PreparedSnapshotPath)) {
    if (-not (Test-Path -LiteralPath (Join-Path $rootPath 'manifest.json') -PathType Leaf)) {
        throw "The explicit authoring snapshot is missing manifest.json: $rootPath"
    }
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('alteros-w5-shared-' + [guid]::NewGuid().ToString('N'))
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $OutputRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputRoot must stay under the system temporary directory.'
}
if (Test-Path -LiteralPath $OutputRoot) { throw 'OutputRoot already exists; choose a new path to preserve earlier artifacts.' }

& (Join-Path $PSScriptRoot 'Check-ContentData.ps1') -Group IntegrateW5 `
    -BaselinePath $BaselineExportPath -PreparedSnapshotPath $PreparedSnapshotPath -BackupPath $BackupPath `
    -ExpectedStoreId $ExpectedStoreId -ExpectedStoreRevision $ExpectedStoreRevision `
    -ExpectedSelectionHash $ExpectedSelectionHash -ExpectedSnapshotHash $ExpectedSnapshotHash -OutputPath $OutputRoot
if ($LASTEXITCODE -ne 0) { throw "W5 shared integration failed. Preserve and inspect $OutputRoot and restore from $BackupPath only if needed." }
