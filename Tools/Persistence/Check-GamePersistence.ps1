param(
    [switch]$RunHarness,
    [string]$HarnessAssembly,
    [string]$SyntheticData,
    [string]$Group = 'All'
)

$ErrorActionPreference = 'Stop'
if ($RunHarness) {
    $testAssembly = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::GetFullPath($HarnessAssembly))
    $entryPoint = $testAssembly.GetType('HeadlessPersistenceTests').GetMethod('Main', [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
    $testArguments = [object[]]::new(1)
    $testArguments[0] = [string[]]@($SyntheticData, $Group)
    exit [int]$entryPoint.Invoke($null, $testArguments)
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unityVersionLine = Select-String -Path (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion:' | Select-Object -First 1
if ($null -eq $unityVersionLine) { throw 'Unity version was not found in ProjectVersion.txt.' }
$unityVersion = ($unityVersionLine.Line -split ':', 2)[1].Trim()
$unityData = Join-Path $env:ProgramFiles ("Unity\Hub\Editor\$unityVersion\Editor\Data")
$monoBin = Join-Path $unityData 'MonoBleedingEdge\bin'
$mono = Join-Path $monoBin 'mono-sgen.exe'
$mcs = Join-Path $unityData 'MonoBleedingEdge\lib\mono\4.5\mcs.exe'
$sqlitePackage = Join-Path $projectRoot 'Library/PackageCache/com.gilzoide.sqlite-net@08248bd5884d'
$sqliteManaged = Join-Path $projectRoot 'Library/ScriptAssemblies/Gilzoide.SqliteNet.dll'
$unityCollections = Join-Path $projectRoot 'Library/ScriptAssemblies/Unity.Collections.dll'
$engineModules = Join-Path $unityData 'Managed\UnityEngine'
$unityCore = Join-Path $engineModules 'UnityEngine.CoreModule.dll'
$unityJson = Join-Path $engineModules 'UnityEngine.JSONSerializeModule.dll'
$newtonsoft = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' |
    ForEach-Object { Join-Path $_.FullName 'Runtime/Newtonsoft.Json.dll' } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
$netstandard = Join-Path $unityData 'MonoBleedingEdge\lib\mono\4.5\Facades\netstandard.dll'
$runtimeAssembly = Join-Path $projectRoot 'Assets/AlterOS/AlterOS.Runtime.asmdef'
$nativePlugin = Join-Path $sqlitePackage 'Plugins/lib/windows/x86_64/gilzoide-sqlite-net.dll'

foreach ($requiredPath in @($mono, $mcs, $sqliteManaged, $unityCollections, $unityCore, $unityJson, $newtonsoft, $netstandard, $runtimeAssembly, $nativePlugin)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required offline persistence dependency is missing: $requiredPath"
    }
}
if ([Environment]::Is64BitProcess -eq $false) { throw 'Run this check from 64-bit PowerShell for the Windows x64 SQLite binding.' }

$runRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('aos-persist-' + [guid]::NewGuid().ToString('N'))
$nativeRoot = Join-Path $runRoot 'native'
New-Item -ItemType Directory -Path $nativeRoot | Out-Null
Copy-Item -LiteralPath $nativePlugin -Destination (Join-Path $nativeRoot 'gilzoide-sqlite-net.dll')
Copy-Item -LiteralPath $nativePlugin -Destination (Join-Path $runRoot 'gilzoide-sqlite-net.dll')
Copy-Item -LiteralPath $sqliteManaged -Destination $runRoot
Copy-Item -LiteralPath $unityCollections -Destination $runRoot
Copy-Item -LiteralPath $unityCore -Destination $runRoot
Copy-Item -LiteralPath $unityJson -Destination $runRoot
Copy-Item -LiteralPath $newtonsoft -Destination $runRoot

$oldPath = $env:PATH
$oldMonoPath = $env:MONO_PATH
$env:PATH = "$nativeRoot;$oldPath"
Remove-Item Env:MONO_PATH -ErrorAction SilentlyContinue

try {
    $runtimeOutput = Join-Path $runRoot 'AlterOS.Persistence.dll'
    $runtimeSources = @(
        (Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Persistence') -Filter '*.cs' -File | ForEach-Object FullName)
        (Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Recording') -Filter '*.cs' -File -ErrorAction SilentlyContinue | ForEach-Object FullName)
        (Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Stats') -Filter '*.cs' -File | ForEach-Object FullName)
        (Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Topics') -Filter '*.cs' -File | ForEach-Object FullName)
        (Join-Path $projectRoot 'Assets/AlterOS/Content/GeneratedArchiveStorage.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Content/GeneratedArchiveImporter.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Content/ContentJson.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Conversation/ConversationStorageContracts.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Conversation/SqliteConversationStore.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Conversation/ConversationSessionImporter.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Conversation/ConversationTranscript.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Conversation/SqliteTranscriptStore.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Conversation/TranscriptJsonlImporter.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Memory/PlayerMemoryContracts.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Memory/MemoryConversationHistoryFilter.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Memory/PlayerMemoryTranscriptSync.cs')
        (Join-Path $projectRoot 'Tools/Persistence/ConversationTranscriptCompileStubs.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Conversation/ConversationModels.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Dialogue/DialogueDefinition.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Dialogue/DialogueValidator.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Dialogue/CanonicalTimeWindow.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Dialogue/CanonicalDialogueEligibilityEvaluator.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Furniture/HomeFurnitureLayout.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Furniture/HomeFurnitureRepository.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Furniture/FurnitureInventory.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Furniture/FurnitureCatalogue.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Furniture/FurnitureArrangementResolver.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Furniture/FurnitureFunctions.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Furniture/FurniturePresentationContracts.cs')
        (Join-Path $projectRoot 'Assets/AlterOS/Furniture/FurniturePowerFunction.cs')
        (Join-Path $projectRoot 'Tools/Persistence/ScenarioItemGrantCompileStubs.cs')
    )
    $runtimeReferences = @(
        "-r:$unityCore",
        "-r:$unityCollections",
        "-r:$sqliteManaged",
        "-r:$newtonsoft",
        "-r:$netstandard",
        '-r:System.Runtime.Serialization.dll'
    )
    & $mono $mcs -langversion:latest -target:library "-out:$runtimeOutput" $runtimeReferences $runtimeSources
    if ($LASTEXITCODE -ne 0) { throw "Persistence source compile failed with exit code $LASTEXITCODE." }

    $sessionFacadeOutput = Join-Path $runRoot 'ConversationSessionFacade.Check.dll'
    $sessionFacadeSources = @(
        (Join-Path $projectRoot 'Tools/Persistence/ConversationSessionCompileStubs.cs'),
        (Join-Path $projectRoot 'Assets/Scripts/Interaction/ConversationSessionStore.cs'),
        (Join-Path $projectRoot 'Assets/Scripts/Interaction/RuntimeMigrationStartup.cs')
    )
    $sessionFacadeReferences = @("-r:$runtimeOutput", "-r:$unityCore", "-r:$netstandard")
    & $mono $mcs -langversion:latest -target:library "-out:$sessionFacadeOutput" $sessionFacadeReferences $sessionFacadeSources
    if ($LASTEXITCODE -ne 0) { throw "Conversation session facade source compile failed with exit code $LASTEXITCODE." }
    Write-Output 'PASS: ConversationSessionStore source compiled against the current SQL store and Unity lifecycle APIs.'

    $harnessOutput = Join-Path $runRoot 'HeadlessPersistenceTests.exe'
    $harnessSources = @(Join-Path $PSScriptRoot 'HeadlessPersistenceTests.cs')
    $harnessReferences = @(
        "-r:$runtimeOutput",
        "-r:$sqliteManaged",
        "-r:$unityCollections",
        "-r:$unityCore",
        "-r:$netstandard",
        '-r:System.Runtime.Serialization.dll'
    )
    & $mono $mcs -langversion:latest -target:exe "-out:$harnessOutput" $harnessReferences $harnessSources
    if ($LASTEXITCODE -ne 0) { throw "Headless test compile failed with exit code $LASTEXITCODE." }

    $env:MONO_PATH = @(
        $runRoot,
        (Join-Path $projectRoot 'Library/ScriptAssemblies'),
        $engineModules,
        (Join-Path $unityData 'Managed'),
        $nativeRoot
    ) -join ';'
    $powershellCommand = Get-Command 'pwsh.exe','powershell.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $powershellCommand) { throw 'A PowerShell executable is required to launch the isolated persistence harness.' }
    & $powershellCommand.Source -NoLogo -NoProfile -File $PSCommandPath -RunHarness `
        -HarnessAssembly $harnessOutput -SyntheticData (Join-Path $runRoot 'synthetic-data') -Group $Group
    if ($LASTEXITCODE -ne 0) { throw "Headless persistence tests failed with exit code $LASTEXITCODE." }
    Write-Output "PASS: Unity $unityVersion bundled Mono compiler; synthetic group '$Group' ran with the real SQLite-net binding and Windows x64 native plugin in an isolated PowerShell process."
    $resolvedRunRoot = [System.IO.Path]::GetFullPath($runRoot)
    $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedRunRoot.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a validation directory outside the system temporary root: $resolvedRunRoot"
    }
    Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
}
catch {
    Write-Output "Synthetic failure artifacts retained at: $runRoot"
    throw
}
finally {
    $env:PATH = $oldPath
    $env:MONO_PATH = $oldMonoPath
}
