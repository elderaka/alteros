param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$versionLine = Select-String -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion:' | Select-Object -First 1
if ($null -eq $versionLine) { throw 'Unity version was not found in ProjectVersion.txt.' }
$unityVersion = ($versionLine.Line -split ':', 2)[1].Trim()
$compiler = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$unityVersion\Editor\Data\DotNetSdkRoslyn\csc.dll"
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$frameworkCompiler = Join-Path $framework 'csc.exe'
$netstandard = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\Facades\netstandard.dll'
$packageCache = Join-Path $projectRoot 'Library/PackageCache'
$sqlitePackage = Join-Path $packageCache 'com.gilzoide.sqlite-net@08248bd5884d'
$sqliteSource = Join-Path $sqlitePackage 'Runtime/sqlite-net/SQLite.cs'
$sqliteNative = Join-Path $sqlitePackage 'Plugins/lib/windows/x86_64/gilzoide-sqlite-net.dll'
$sqliteLicense = Join-Path $sqlitePackage 'Plugins/sqlite-net~/LICENSE.txt'
$packageLicense = Join-Path $sqlitePackage 'LICENSE.txt'
$newtonsoft = Get-ChildItem -LiteralPath $packageCache -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' |
    ForEach-Object { Join-Path $_.FullName 'Runtime/Newtonsoft.Json.dll' } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
$defaultOutputRoot = Join-Path $PSScriptRoot 'CoreHost/bin'
$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $defaultOutputRoot
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$output = Join-Path $outputRoot 'AlterOS.ContentWorkbench.exe'

foreach ($requiredPath in @($compiler, $frameworkCompiler, $netstandard, $sqliteSource, $sqliteNative, $sqliteLicense, $packageLicense, $newtonsoft)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) { throw "Required local Workbench dependency is missing: $requiredPath" }
}
if ([Environment]::Is64BitProcess -eq $false) { throw 'Run this build from 64-bit PowerShell for the Windows x64 SQLite binding.' }

$frameworkReferences = @('mscorlib.dll','System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.Web.Extensions.dll','System.Xml.dll','Microsoft.CSharp.dll') |
    ForEach-Object { Join-Path $framework $_ }
foreach ($reference in $frameworkReferences) {
    if (-not (Test-Path -LiteralPath $reference -PathType Leaf)) { throw "Required .NET Framework reference missing: $reference" }
}
$references = @($frameworkReferences | ForEach-Object { '/reference:' + $_ })
$references += '/reference:' + $netstandard
$references += '/reference:' + $newtonsoft

$sourceRoots = @('Assets/AlterOS/Stats','Assets/AlterOS/Topics','Assets/AlterOS/Scenarios',
    'Assets/AlterOS/Content','Assets/AlterOS/Authoring')
$sources = @()
foreach ($sourceRoot in $sourceRoots) {
    $sourceDirectory = Join-Path $projectRoot $sourceRoot
    if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) { throw "Required source directory not found: $sourceRoot" }
    $sources += Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' -File | Select-Object -ExpandProperty FullName
}
$sources += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Dialogue') -Filter '*.cs' -File |
    Where-Object { $_.Name -ne 'AuthoredDialogueVariantSelector.cs' } | Select-Object -ExpandProperty FullName
$sources += @(
    'ConversationContracts.cs','ConversationContractValidator.cs','ConversationProtocol.cs',
    'ConversationCoverage.cs','ScenarioTurnProtocol.cs'
) | ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Conversation/' + $_) }
$sources += @('PlayerMemoryContracts.cs','PlayerMemoryProtocol.cs') |
    ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Memory/' + $_) }
$sources += Join-Path $projectRoot 'Assets/AlterOS/Recording/RecordingContracts.cs'
$sources += @(
    'ActivityExecutionContracts.cs','ActivityPrimitiveRegistry.cs','ActivitySequenceDefinition.cs','ActivitySequenceValidator.cs','ActivitySequenceBuilder.cs',
    'ActivitySequenceRunner.cs','ActivitySequenceReleaseAdapter.cs','ActivitySequenceExecutor.cs',
    'ActivitySequencePrimitiveDispatcher.cs','ActivitySequenceBarkOperation.cs'
) | ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Activities/' + $_) }
$sources += @(
    'FurnitureCatalogue.cs','FurnitureCatalogueRegistry.cs','FurnitureCatalogueValidator.cs','FurnitureFunctions.cs',
    'FurniturePowerFunction.cs','FurniturePresentationContracts.cs','FurnitureFunctionTargetAdapter.cs'
) | ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Furniture/' + $_) }
$sources += @('TopicRoutePolicy.cs','TopicFamilySemanticMatcher.cs') |
    ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Semantics/' + $_) }
$sources += @(
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchCommandHost.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchCommands.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchDraftStore.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchAuthoringStore.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchSchemaCommands.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchPreview.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchPreviewSourceBuilder.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchPreviewRuntime.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchDialoguePreview.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchItemPreview.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchFurniturePreview.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchActionPreview.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchTopicPreview.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchScenarioPreview.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchSyntheticHostEffectPort.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchFixtureActivitySequenceRunner.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/WorkbenchPreviewFlowEffectDriver.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/Compatibility/Mathf.cs'),
    (Join-Path $PSScriptRoot 'CoreHost/Compatibility/DialogueRouteMatch.cs'),
    (Join-Path $PSScriptRoot '..\..\Tools\ContentData\SqlitePreserveAttributeShim.cs'),
    $sqliteSource
)
foreach ($source in $sources) { if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required source not found: $source" } }

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
& dotnet $compiler /nologo /noconfig /nostdlib+ /platform:x64 /target:exe /langversion:latest "/out:$output" $references $sources
if ($LASTEXITCODE -ne 0) { throw 'Workbench C# host compilation failed.' }

Copy-Item -LiteralPath $netstandard -Destination $outputRoot -Force
Copy-Item -LiteralPath $newtonsoft -Destination $outputRoot -Force
Copy-Item -LiteralPath $sqliteNative -Destination $outputRoot -Force
Copy-Item -LiteralPath $sqliteLicense -Destination (Join-Path $outputRoot 'sqlite-net-LICENSE.txt') -Force
Copy-Item -LiteralPath $packageLicense -Destination (Join-Path $outputRoot 'Gilzoide-SqliteNet-LICENSE.txt') -Force
Write-Output "Built standalone x64 $output with the pinned SQLite dependency and no Unity runtime references."
