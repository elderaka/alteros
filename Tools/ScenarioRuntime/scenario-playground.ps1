param(
    [Parameter(Mandatory = $true)]
    [string]$Fixture,

    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$fixturePath = (Resolve-Path -LiteralPath $Fixture).Path
$null = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json

if (!(Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity Editor executable not found: $UnityPath"
}

$activeEditor = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
    Where-Object {
        $_.CommandLine -and
        $_.CommandLine.IndexOf($projectRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -and
        $_.CommandLine -notmatch '(^|\s)-batchmode(\s|$)'
    }

if ($activeEditor) {
    throw 'Close the AlterOS Unity Editor before running the headless Scenario Playground command. Use the Topic Playground preview while Unity is open.'
}

$logPath = Join-Path $env:TEMP ("AlterOS-ScenarioPlayground-{0}.log" -f [guid]::NewGuid().ToString('N'))
& $UnityPath `
    -batchmode `
    -nographics `
    -projectPath $projectRoot `
    -logFile $logPath `
    -executeMethod AlterOS.Editor.TopicPlaygroundCommand.RunScenarioFixture `
    -scenarioFixture $fixturePath
$unityExitCode = $LASTEXITCODE

$marker = 'SCENARIO_PLAYGROUND_JSON '
$resultLine = Get-Content -LiteralPath $logPath -ErrorAction SilentlyContinue |
    Where-Object { $_.Contains($marker) } |
    Select-Object -Last 1

if ($resultLine) {
    $markerIndex = $resultLine.IndexOf($marker, [System.StringComparison]::Ordinal)
    Write-Output $resultLine.Substring($markerIndex + $marker.Length)
} else {
    Write-Error "Unity did not produce a Scenario Playground result. Log: $logPath"
    Get-Content -LiteralPath $logPath -Tail 50 -ErrorAction SilentlyContinue
}

if ($unityExitCode -ne 0) { exit $unityExitCode }
if (!$resultLine) { exit 2 }
