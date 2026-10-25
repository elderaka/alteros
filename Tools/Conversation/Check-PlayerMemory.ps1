param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$python = Join-Path $projectRoot 'Tools/CompanionRuntime/.venv/Scripts/python.exe'
$helper = Join-Path $projectRoot 'Build/CompanionRuntimeM2Task6FinalAcceptance/AlterOSCompanion/AlterOSCompanion.exe'
if (!(Test-Path -LiteralPath $python)) { throw "Project Python runtime is missing: $python" }
if (!(Test-Path -LiteralPath $helper)) { throw "Fresh packaged helper is missing: $helper" }

$oldHelper = $env:ALTEROS_PACKAGED_HELPER
$env:ALTEROS_PACKAGED_HELPER = $helper
Push-Location (Join-Path $projectRoot 'Tools/CompanionRuntime')
try {
    & $python -m pytest tests -q
    if ($LASTEXITCODE -ne 0) { throw "Python companion suite failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
    if ($null -eq $oldHelper) { Remove-Item Env:ALTEROS_PACKAGED_HELPER -ErrorAction SilentlyContinue }
    else { $env:ALTEROS_PACKAGED_HELPER = $oldHelper }
}

Push-Location $projectRoot
try {
    & (Join-Path $projectRoot 'Tools/Conversation/Check-UnifiedConversation.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Conversation and memory C# source checks failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }
