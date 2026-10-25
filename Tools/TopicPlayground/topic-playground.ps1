[CmdletBinding(DefaultParameterSetName = 'Scenario')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Scenario')]
    [string]$Scenario,

    [Parameter(Mandatory = $true, ParameterSetName = 'SqlQuery')]
    [string[]]$Query,
    [Parameter(ParameterSetName = 'SqlQuery')]
    [string[]]$ExpectedTopicId,
    [Parameter(ParameterSetName = 'SqlQuery')]
    [string]$ArtifactPath,
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe'
)

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ($PSCmdlet.ParameterSetName -eq 'SqlQuery') {
    $ErrorActionPreference = 'Stop'
    if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'The installed SQL sentence gate requires PowerShell 7.' }
    if ($ExpectedTopicId -and $ExpectedTopicId.Count -ne $Query.Count) { throw 'Supply one ExpectedTopicId per Query, or omit expectations.' }
    if (-not $ArtifactPath) { $ArtifactPath = Join-Path $projectRoot 'Assets/StreamingAssets/Content/content.sqlite3' }
    $vectors = Join-Path $env:TEMP ('AlterOS-route-probe-' + [guid]::NewGuid().ToString('N') + '.json')
    $encodeArguments = @((Join-Path $projectRoot 'Tools/SemanticLab/encode_route_probes.py'),'--output',$vectors)
    foreach ($text in $Query) { $encodeArguments += @('--query',$text) }
    foreach ($id in $ExpectedTopicId) { $encodeArguments += @('--expected-topic',$id) }
    & (Join-Path $projectRoot 'Tools/SemanticLab/.venv/Scripts/python.exe') @encodeArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & (Join-Path $projectRoot 'Tools/ContentData/Check-ContentData.ps1') -Group ProbeRoutes -ArtifactPath $ArtifactPath -ManifestPath $vectors
    exit $LASTEXITCODE
}
$scenarioPath = (Resolve-Path -LiteralPath $Scenario).Path

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
    throw 'Close the AlterOS Unity Editor before running the headless command. The Editor window can be used while Unity is open.'
}

$logPath = Join-Path $env:TEMP ("AlterOS-TopicPlayground-{0}.log" -f [guid]::NewGuid().ToString('N'))
& $UnityPath `
    -batchmode `
    -nographics `
    -projectPath $projectRoot `
    -logFile $logPath `
    -executeMethod AlterOS.Editor.TopicPlaygroundCommand.Run `
    -topicScenario $scenarioPath
$unityExitCode = $LASTEXITCODE

$marker = 'TOPIC_PLAYGROUND_JSON '
$resultLine = Get-Content -LiteralPath $logPath -ErrorAction SilentlyContinue |
    Where-Object { $_.Contains($marker) } |
    Select-Object -Last 1

if ($resultLine) {
    $markerIndex = $resultLine.IndexOf($marker, [System.StringComparison]::Ordinal)
    Write-Output $resultLine.Substring($markerIndex + $marker.Length)
} else {
    Write-Error "Unity did not produce a Topic Playground result. Log: $logPath"
    Get-Content -LiteralPath $logPath -Tail 50 -ErrorAction SilentlyContinue
}

if ($unityExitCode -ne 0) {
    exit $unityExitCode
}

if (!$resultLine) {
    exit 2
}
