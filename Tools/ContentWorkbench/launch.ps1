$ErrorActionPreference = 'Stop'
$python = Get-Command python -ErrorAction SilentlyContinue
if ($null -eq $python) { throw 'Python 3 was not found on PATH.' }
$hostPath = Join-Path $PSScriptRoot 'CoreHost/bin/AlterOS.ContentWorkbench.exe'
if (!(Test-Path -LiteralPath $hostPath -PathType Leaf)) {
    throw 'Workbench command host is not built. Run build-core.ps1 first.'
}
& $python.Source (Join-Path $PSScriptRoot 'server.py')
exit $LASTEXITCODE
