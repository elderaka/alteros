param(
    [switch]$RunHarness,
    [string]$HarnessAssembly,
    [string]$SyntheticData,
    [string]$RuntimeAssembly,
    [string]$ManifestPath,
    [string]$OutputPath,
    [string]$ArtifactPath,
    [string]$BaselinePath,
    [string]$PreparedSnapshotPath,
    [string]$BackupPath,
    [string]$ExpectedStoreId,
    [string]$ExpectedStoreRevision,
    [string]$ExpectedSelectionHash,
    [string]$ExpectedSnapshotHash,
    [ValidateSet('Repository','Bake','Publish','Flows','Semantics','JsonCompatibility','Authoring','ReusableDefinitions','ContentSchemaMigration','FurnitureCatalogueBridge','GteTopicAnchors','ProbeRoutes','BakeCommand','InspectArtifact','PrepareW5','IntegrateW5')]
    [string]$Group = 'Repository'
)

$ErrorActionPreference = 'Stop'
if ($RunHarness) {
    $testAssembly = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::GetFullPath($HarnessAssembly))
    if ($Group -eq 'PrepareW5') {
        $entryPoint = $testAssembly.GetType('PrepareW5ProofRelease').GetMethod('Main', [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
        $projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
        $preparerArguments = [object[]]::new(1)
        $preparerArguments[0] = [string[]]@($ManifestPath, $OutputPath, $projectRoot)
        exit [int]$entryPoint.Invoke($null, $preparerArguments)
    }
    if ($Group -eq 'IntegrateW5') {
        $entryPoint = $testAssembly.GetType('PrepareW5ProofRelease').GetMethod('Main', [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
        $projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
        $preparerArguments = [object[]]::new(1)
        $preparerArguments[0] = [string[]]@('integrate', $BaselinePath, $PreparedSnapshotPath, $OutputPath, $projectRoot,
            $ExpectedStoreId, $ExpectedStoreRevision, $ExpectedSelectionHash, $ExpectedSnapshotHash, $BackupPath)
        exit [int]$entryPoint.Invoke($null, $preparerArguments)
    }
    $entryPoint = $testAssembly.GetType('HeadlessContentDataTests').GetMethod('Main', [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
    $testArguments = [object[]]::new(1)
    if ($Group -eq 'ProbeRoutes') {
        if ([string]::IsNullOrWhiteSpace($ManifestPath) -or [string]::IsNullOrWhiteSpace($ArtifactPath)) { throw 'ProbeRoutes requires -ManifestPath (query vectors) and -ArtifactPath (installed SQLite).' }
        $testArguments[0] = [string[]]@($Group, $SyntheticData, $RuntimeAssembly, $ArtifactPath, $ManifestPath)
    } elseif ($Group -eq 'BakeCommand') {
        $testArguments[0] = [string[]]@($Group, $SyntheticData, $RuntimeAssembly, $ManifestPath, $OutputPath)
    } elseif ($Group -eq 'InspectArtifact') {
        $testArguments[0] = [string[]]@($Group, $SyntheticData, $RuntimeAssembly, $ArtifactPath)
    } else {
        $testArguments[0] = [string[]]@($Group, $SyntheticData, $RuntimeAssembly)
    }
    exit [int]$entryPoint.Invoke($null, $testArguments)
}

if ($Group -eq 'BakeCommand' -and [string]::IsNullOrWhiteSpace($OutputPath)) {
    throw 'BakeCommand requires an explicit -OutputPath value.'
}
if ($Group -eq 'InspectArtifact' -and [string]::IsNullOrWhiteSpace($ArtifactPath)) {
    throw 'InspectArtifact requires an explicit -ArtifactPath value.'
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unityVersionLine = Select-String -Path (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion:' | Select-Object -First 1
if ($null -eq $unityVersionLine) { throw 'Unity version was not found in ProjectVersion.txt.' }
$unityVersion = ($unityVersionLine.Line -split ':', 2)[1].Trim()
$unityData = Join-Path $env:ProgramFiles ("Unity\Hub\Editor\$unityVersion\Editor\Data")
$mono = Join-Path $unityData 'MonoBleedingEdge\bin\mono-sgen.exe'
$mcs = Join-Path $unityData 'MonoBleedingEdge\lib\mono\4.5\mcs.exe'
$roslyn = Join-Path $unityData 'DotNetSdkRoslyn\csc.dll'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$sqlitePackage = Join-Path $projectRoot 'Library/PackageCache/com.gilzoide.sqlite-net@08248bd5884d'
$sqliteManaged = Join-Path $projectRoot 'Library/ScriptAssemblies/Gilzoide.SqliteNet.dll'
$unityCollections = Join-Path $projectRoot 'Library/ScriptAssemblies/Unity.Collections.dll'
$engineModules = Join-Path $unityData 'Managed\UnityEngine'
$unityCore = Join-Path $engineModules 'UnityEngine.CoreModule.dll'
$unityJson = Join-Path $engineModules 'UnityEngine.JSONSerializeModule.dll'
$netstandard = Join-Path $unityData 'MonoBleedingEdge\lib\mono\4.5\Facades\netstandard.dll'
$newtonsoft = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' |
    ForEach-Object { Join-Path $_.FullName 'Runtime/Newtonsoft.Json.dll' } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
$serialization = Join-Path $unityData 'MonoBleedingEdge\lib\mono\4.5\System.Runtime.Serialization.dll'
$nativePlugin = Join-Path $sqlitePackage 'Plugins/lib/windows/x86_64/gilzoide-sqlite-net.dll'

foreach ($requiredPath in @($mono, $mcs, $roslyn, $sqliteManaged, $unityCollections, $unityCore, $unityJson, $netstandard, $newtonsoft, $serialization, $nativePlugin)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required offline content-data dependency is missing: $requiredPath"
    }
}
if ([Environment]::Is64BitProcess -eq $false) { throw 'Run this check from 64-bit PowerShell for the Windows x64 SQLite binding.' }

$runRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('aos-contentdata-' + [guid]::NewGuid().ToString('N'))
$nativeRoot = Join-Path $runRoot 'native'
New-Item -ItemType Directory -Path $nativeRoot -Force | Out-Null
Copy-Item -LiteralPath $nativePlugin -Destination (Join-Path $nativeRoot 'gilzoide-sqlite-net.dll')
Copy-Item -LiteralPath $nativePlugin -Destination (Join-Path $runRoot 'gilzoide-sqlite-net.dll')
Copy-Item -LiteralPath $sqliteManaged -Destination $runRoot
Copy-Item -LiteralPath $unityCollections -Destination $runRoot
Copy-Item -LiteralPath $unityCore -Destination $runRoot
Copy-Item -LiteralPath $unityJson -Destination $runRoot
Copy-Item -LiteralPath $newtonsoft -Destination $runRoot
Copy-Item -LiteralPath $serialization -Destination $runRoot

$oldPath = $env:PATH
$oldMonoPath = $env:MONO_PATH
$env:PATH = "$runRoot;$nativeRoot;$oldPath"
try {
    $runtimeOutput = Join-Path $runRoot 'AlterOS.Content.dll'
    $runtimeSources = @(Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Content') -Filter '*.cs' -File | ForEach-Object FullName)
    $runtimeSources += @('ActivitySequenceDefinition.cs','ActivityPrimitiveRegistry.cs','ActivitySequenceValidator.cs','ActivitySequenceBuilder.cs','ActivitySequenceRunner.cs','ActivitySequenceReleaseAdapter.cs') |
        ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Activities/' + $_) }
    $runtimeSources += @('FurnitureCatalogue.cs','FurnitureCatalogueValidator.cs','FurnitureCatalogueRegistry.cs','FurnitureFunctions.cs','FurniturePowerFunction.cs','FurniturePresentationContracts.cs') |
        ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Furniture/' + $_) }
    if ($runtimeSources.Count -eq 0) { throw 'Content source files are missing.' }
    $runtimeSources += @(Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Authoring') -Filter '*.cs' -File | ForEach-Object FullName)
    $runtimeSources += @(Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Stats') -Filter '*.cs' -File | ForEach-Object FullName)
    $runtimeSources += @('DialogueDefinition.cs','DialogueValidator.cs','AuthoredDialogueRouteCatalog.cs','AuthoredDialogueVariantSelector.cs','CanonicalTimeWindow.cs','CanonicalDialogueEligibilityEvaluator.cs') |
        ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Dialogue/' + $_) }
    $runtimeSources += @(Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Topics') -Filter '*.cs' -File | ForEach-Object FullName)
    $runtimeSources += @('TopicRoutePolicy.cs','DialogueRouteIndex.cs','TopicFamilySemanticMatcher.cs') |
        ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Semantics/' + $_) }
    $runtimeSources += @(Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Conversation') -Filter '*.cs' -File | ForEach-Object FullName)
    $runtimeSources += @(Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Memory') -Filter '*.cs' -File | ForEach-Object FullName)
    $runtimeSources += @(Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Recording') -Filter '*.cs' -File | ForEach-Object FullName)
    $runtimeSources += @(Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Scenarios') -Filter '*.cs' -File | ForEach-Object FullName)
    $runtimeSources += @(
        (Join-Path $projectRoot 'Assets/AlterOS/Persistence/GamePersistenceContracts.cs'),
        (Join-Path $projectRoot 'Assets/AlterOS/Persistence/GameHistoryContracts.cs'),
        (Join-Path $projectRoot 'Assets/AlterOS/Persistence/GameSchemaMigrations.cs'),
        (Join-Path $projectRoot 'Assets/AlterOS/Persistence/GameOperationId.cs'),
        (Join-Path $projectRoot 'Tools/ContentData/GamePersistenceCompileStubs.cs'),
        (Join-Path $projectRoot 'Assets/AlterOS/Persistence/ScenarioItemGrantPersistenceAdapter.cs')
    )
    $runtimeReferences = @('mscorlib.dll','System.dll','System.Core.dll','Microsoft.CSharp.dll','System.Runtime.Serialization.dll','System.Xml.dll') |
        ForEach-Object { '/reference:' + (Join-Path $framework $_) }
    $runtimeReferences += @("/reference:$sqliteManaged", "/reference:$unityCollections", "/reference:$netstandard",
        "/reference:$newtonsoft", "/reference:$serialization", "/reference:$unityCore", "/reference:$unityJson")
    & dotnet $roslyn /nologo /noconfig /nostdlib+ /target:library "/out:$runtimeOutput" $runtimeReferences $runtimeSources
    if ($LASTEXITCODE -ne 0) { throw "Content source compile failed with exit code $LASTEXITCODE." }

    if ($Group -eq 'PrepareW5' -or $Group -eq 'IntegrateW5') {
        if ([string]::IsNullOrWhiteSpace($OutputPath)) { throw "$Group requires -OutputPath (a new directory under %TEMP%)." }
        if ($Group -eq 'PrepareW5' -and [string]::IsNullOrWhiteSpace($ManifestPath)) {
            throw 'PrepareW5 requires -ManifestPath (the exact explicit authoring export).'
        }
        $preparerOutput = Join-Path $runRoot 'PrepareW5ProofRelease.exe'
        $preparerSource = Join-Path $PSScriptRoot 'PrepareW5ProofRelease.cs'
        $preparerReferences = @("-r:$sqliteManaged", "-r:$unityCollections", "-r:$unityCore", "-r:$unityJson", "-r:$netstandard", "-r:$serialization", "-r:$newtonsoft", "-r:$runtimeOutput")
        & $mono $mcs -langversion:latest -target:exe "-out:$preparerOutput" $preparerReferences $preparerSource
        if ($LASTEXITCODE -ne 0) { throw "W5 proof-release preparation tool compile failed with exit code $LASTEXITCODE." }
        $env:MONO_PATH = @($runRoot, (Join-Path $projectRoot 'Library/ScriptAssemblies'), $engineModules,
            (Join-Path $unityData 'Managed'), $nativeRoot) -join ';'
        $powershellCommand = Get-Command 'pwsh.exe','powershell.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $powershellCommand) { throw 'A PowerShell executable is required to run the staged W5 preparation tool.' }
        if ($Group -eq 'PrepareW5') {
            & $powershellCommand.Source -NoLogo -NoProfile -File $PSCommandPath -RunHarness `
                -HarnessAssembly $preparerOutput -ManifestPath $ManifestPath -OutputPath $OutputPath -Group PrepareW5
        } else {
            foreach ($required in @($BaselinePath, $PreparedSnapshotPath, $BackupPath, $ExpectedStoreId, $ExpectedStoreRevision, $ExpectedSelectionHash, $ExpectedSnapshotHash)) {
                if ([string]::IsNullOrWhiteSpace($required)) { throw 'IntegrateW5 requires explicit baseline/prepared exports, backup path, expected store ID/revision, selection hash and snapshot hash.' }
            }
            & $powershellCommand.Source -NoLogo -NoProfile -File $PSCommandPath -RunHarness `
                -HarnessAssembly $preparerOutput -BaselinePath $BaselinePath -PreparedSnapshotPath $PreparedSnapshotPath `
                -BackupPath $BackupPath -ExpectedStoreId $ExpectedStoreId -ExpectedStoreRevision $ExpectedStoreRevision `
                -ExpectedSelectionHash $ExpectedSelectionHash -ExpectedSnapshotHash $ExpectedSnapshotHash `
                -OutputPath $OutputPath -Group IntegrateW5
        }
        if ($LASTEXITCODE -ne 0) { throw "W5 proof-release preparation failed with exit code $LASTEXITCODE. Output: $OutputPath" }
        $resolvedRunRoot = [System.IO.Path]::GetFullPath($runRoot)
        $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $resolvedRunRoot.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to remove a preparation directory outside the system temporary root: $resolvedRunRoot" }
        Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
        exit 0
    }

    $harnessOutput = Join-Path $runRoot 'HeadlessContentDataTests.exe'
    $references = @("-r:$sqliteManaged", "-r:$unityCollections", "-r:$unityCore", "-r:$unityJson", "-r:$netstandard", "-r:$serialization", "-r:$newtonsoft")
    $references += "-r:$runtimeOutput"
    $source = Join-Path $PSScriptRoot 'HeadlessContentDataTests.cs'
    & $mono $mcs -langversion:latest -target:exe "-out:$harnessOutput" $references $source
    if ($LASTEXITCODE -ne 0) { throw "Headless content-data test compile failed with exit code $LASTEXITCODE." }

    $syntheticRoot = Join-Path $runRoot 'synthetic'
    $env:MONO_PATH = @($runRoot, (Join-Path $projectRoot 'Library/ScriptAssemblies'), $engineModules,
        (Join-Path $unityData 'Managed'), $nativeRoot) -join ';'
    $powershellCommand = Get-Command (Join-Path $PSHOME 'pwsh.exe'),'pwsh.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $powershellCommand) { throw 'PowerShell 7 is required to launch the isolated content-data harness.' }
    $harnessArguments = @('-NoLogo','-NoProfile','-File',$PSCommandPath,'-RunHarness',
        '-HarnessAssembly',$harnessOutput,'-SyntheticData',$syntheticRoot,'-RuntimeAssembly',$runtimeOutput,'-Group',$Group)
    foreach ($optional in @{ ManifestPath = $ManifestPath; OutputPath = $OutputPath; ArtifactPath = $ArtifactPath }.GetEnumerator()) {
        if (-not [string]::IsNullOrWhiteSpace($optional.Value)) { $harnessArguments += @(('-' + $optional.Key), $optional.Value) }
    }
    & $powershellCommand.Source @harnessArguments
    if ($LASTEXITCODE -ne 0) { throw "Content-data group $Group failed with exit code $LASTEXITCODE. Artifacts: $runRoot" }

    $resolvedRunRoot = [System.IO.Path]::GetFullPath($runRoot)
    $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedRunRoot.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a validation directory outside the system temporary root: $resolvedRunRoot"
    }
    Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
}
finally {
    $env:PATH = $oldPath
    $env:MONO_PATH = $oldMonoPath
}
