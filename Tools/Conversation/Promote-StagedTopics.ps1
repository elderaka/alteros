param(
    [string]$StagedRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'TopicRuntime\StagedCatalogue'),
    [string]$ProductionRoot = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'Assets\StreamingAssets\Dialogue\Topics')
)

$ErrorActionPreference = 'Stop'
$staged = [System.IO.Path]::GetFullPath($StagedRoot)
$production = [System.IO.Path]::GetFullPath($ProductionRoot)
$parent = Split-Path -Parent $production
if (!(Test-Path -LiteralPath $staged -PathType Container)) { throw 'Staged Topic catalogue was not found.' }
if (Test-Path -LiteralPath $production) { throw "Production target already exists; refusing to overwrite: $production" }

$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$validator = Join-Path $projectRoot 'Tools\TopicRuntime\Validate-TopicCatalogue.ps1'
$result = & powershell -NoProfile -ExecutionPolicy Bypass -File $validator -CataloguePath $staged
if ($LASTEXITCODE -ne 0 -or !$result.StartsWith('PASS:', [StringComparison]::Ordinal)) {
    throw 'Staged Topic catalogue failed the production validator.'
}

$temporary = Join-Path $parent ('.Topics-promote-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $sourceFiles = @(Get-ChildItem -LiteralPath $staged -File -Recurse | Sort-Object FullName)
    foreach ($source in $sourceFiles) {
        $relative = $source.FullName.Substring($staged.Length).TrimStart('\', '/')
        $destination = Join-Path $temporary $relative
        $destinationDirectory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        Copy-Item -LiteralPath $source.FullName -Destination $destination
        $sourceHash = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($sourceHash -ne $destinationHash) { throw "Byte verification failed for $relative" }
    }

    $result = & powershell -NoProfile -ExecutionPolicy Bypass -File $validator -CataloguePath $temporary
    if ($LASTEXITCODE -ne 0 -or !$result.StartsWith('PASS:', [StringComparison]::Ordinal)) {
        throw 'Promoted Topic payload failed the production validator.'
    }
    Move-Item -LiteralPath $temporary -Destination $production
    $temporary = $null
    "PASS: $result Topic payloads copied byte-for-byte. Loader remains disabled until M2 acceptance."
}
finally {
    if ($temporary -and (Test-Path -LiteralPath $temporary)) {
        $resolvedTemporary = [System.IO.Path]::GetFullPath($temporary)
        if (!$resolvedTemporary.StartsWith([System.IO.Path]::GetFullPath($parent), [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove a promotion temporary outside the intended directory.'
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
