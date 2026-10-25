param(
    [string]$OutputFolder = 'Build/CompanionRuntime',
    [string]$WorkFolder = 'Build/CompanionRuntimeWork'
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$distPath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $OutputFolder))
$workPath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $WorkFolder))
$sourcePath = Join-Path $PSScriptRoot 'src'
$personaPath = Join-Path $sourcePath 'alteros_companion/persona.json'
$entryPoint = Join-Path $PSScriptRoot 'build_entrypoint.py'

if (Test-Path -LiteralPath $distPath) { throw "Build output already exists; preserve or remove it explicitly before rebuilding: $distPath" }
if (Test-Path -LiteralPath $workPath) { throw "Build work directory already exists; preserve or remove it explicitly before rebuilding: $workPath" }

$arguments = @(
    'run', '--locked', '--group', 'build', 'pyinstaller',
    '--onedir', '--console', '--contents-directory', '.',
    '--name', 'AlterOSCompanion',
    '--paths', $sourcePath,
    '--add-data', ($personaPath + ':alteros_companion'),
    '--specpath', $workPath,
    '--distpath', $distPath,
    '--workpath', $workPath,
    $entryPoint
)
Push-Location $PSScriptRoot
try {
    & uv @arguments
    if ($LASTEXITCODE -ne 0) { throw "PyInstaller failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }
