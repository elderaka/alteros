param([string]$Filter = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('AlterOS-ScenarioRuntime-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$compiler = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\DotNetSdkRoslyn\csc.dll'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Unity bundled Roslyn compiler not found.' }
$refs = @('mscorlib.dll','System.dll','System.Core.dll','Microsoft.CSharp.dll','System.Runtime.Serialization.dll','System.Xml.dll') |
    ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$unityCore = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\Managed\UnityEngine\UnityEngine.CoreModule.dll'
$unityJson = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\Managed\UnityEngine\UnityEngine.JSONSerializeModule.dll'
$facade = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\MonoBleedingEdge\lib\mono\4.5\Facades\netstandard.dll'
$unityData = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data'
$newtonsoft = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' |
    ForEach-Object { Join-Path $_.FullName 'Runtime/Newtonsoft.Json.dll' } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
$sqlitePackage = Join-Path $projectRoot 'Library/PackageCache/com.gilzoide.sqlite-net@08248bd5884d'
$serialization = Join-Path $unityData 'MonoBleedingEdge\lib\mono\4.5\System.Runtime.Serialization.dll'
$sqliteManaged = Join-Path $projectRoot 'Library/ScriptAssemblies/Gilzoide.SqliteNet.dll'
$unityCollections = Join-Path $projectRoot 'Library/ScriptAssemblies/Unity.Collections.dll'
$nativePlugin = Join-Path $sqlitePackage 'Plugins/lib/windows/x86_64/gilzoide-sqlite-net.dll'
foreach ($dependency in @($sqliteManaged, $unityCollections, $nativePlugin)) {
    if (!(Test-Path -LiteralPath $dependency -PathType Leaf)) { throw "Content authoring dependency is missing: $dependency" }
}
$refs += @("/reference:$unityCore", "/reference:$unityJson", "/reference:$facade", "/reference:$newtonsoft",
    "/reference:$serialization", "/reference:$sqliteManaged", "/reference:$unityCollections")
[Reflection.Assembly]::LoadFrom($unityCore) | Out-Null
[Reflection.Assembly]::LoadFrom($unityJson) | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Stats') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += Join-Path $projectRoot 'Assets/AlterOS/Activities/ActivityContracts.cs'
$sources += @('FurnitureCatalogue.cs','FurnitureCatalogueValidator.cs','FurnitureFunctions.cs','FurniturePresentationContracts.cs','FurniturePowerFunction.cs') |
    ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Furniture/' + $_) }
$sources += @(
    'Assets/AlterOS/Activities/ActivityExecutionContracts.cs',
    'Assets/AlterOS/Activities/ActivityHost.cs',
    'Assets/AlterOS/Activities/ActivitySequenceDefinition.cs',
    'Assets/AlterOS/Activities/ActivitySequenceExecution.cs',
    'Assets/AlterOS/Activities/ActivitySequencePrimitiveDispatcher.cs',
    'Assets/AlterOS/Activities/ActivitySequenceBarkOperation.cs',
    'Assets/AlterOS/Activities/ActivityPrimitiveRegistry.cs',
    'Assets/AlterOS/Activities/ActivitySequenceBuilder.cs',
    'Assets/AlterOS/Activities/ActivitySequenceValidator.cs',
    'Assets/AlterOS/Activities/ActivitySequenceRunner.cs',
    'Assets/AlterOS/Activities/ActivitySequenceExecutor.cs',
    'Assets/AlterOS/Activities/ActivitySequenceReleaseAdapter.cs'
) | ForEach-Object { Join-Path $projectRoot $_ }
$sources += @('DialogueDefinition.cs','DialogueValidator.cs','CanonicalTimeWindow.cs','CanonicalDialogueEligibilityEvaluator.cs') |
    ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Dialogue/' + $_) }
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Topics') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += @('TopicRoutePolicy.cs','DialogueRouteIndex.cs','TopicFamilySemanticMatcher.cs') |
    ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Semantics/' + $_) }
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Conversation') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += Join-Path $projectRoot 'Assets/AlterOS/Recording/RecordingContracts.cs'
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Memory') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$scenarioRoot = Join-Path $projectRoot 'Assets/AlterOS/Scenarios'
if (Test-Path -LiteralPath $scenarioRoot) {
    $sources += @(Get-ChildItem -LiteralPath $scenarioRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName)
}
$contentRoot = Join-Path $projectRoot 'Assets/AlterOS/Content'
if (Test-Path -LiteralPath $contentRoot) {
    $sources += @(Get-ChildItem -LiteralPath $contentRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName)
}
$authoringRoot = Join-Path $projectRoot 'Assets/AlterOS/Authoring'
$sources += @(Get-ChildItem -LiteralPath $authoringRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$runtimeDll = Join-Path $scratch 'AlterOS.Scenario.Runtime.dll'
& dotnet $compiler /nologo /noconfig /nostdlib+ /target:library "/out:$runtimeDll" $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Scenario runtime source compilation failed.' }
[Reflection.Assembly]::LoadFrom($newtonsoft) | Out-Null
$netstandardRuntime = Join-Path $scratch 'netstandard.dll'
Copy-Item -LiteralPath $facade -Destination $netstandardRuntime
[Reflection.Assembly]::LoadFrom($netstandardRuntime) | Out-Null
[Reflection.Assembly]::LoadFrom($sqliteManaged) | Out-Null
[Reflection.Assembly]::LoadFrom($unityCollections) | Out-Null
[Reflection.Assembly]::LoadFrom($runtimeDll) | Out-Null
$nunit = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.ext.nunit@*' |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -Filter nunit.framework.dll } |
    Select-Object -First 1 -ExpandProperty FullName
if (!$nunit) { throw 'Installed NUnit not found.' }
[Reflection.Assembly]::LoadFrom($nunit) | Out-Null
$testRoot = Join-Path $projectRoot 'Assets/AlterOS/Tests/Editor/Scenarios'
$testSources = @(Get-ChildItem -LiteralPath $testRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$flowHarnessRoot = Join-Path $projectRoot 'Tools/ScenarioRuntime'
$testSources += @(Get-ChildItem -LiteralPath $flowHarnessRoot -Filter '*Flow*Tests.cs' | Select-Object -ExpandProperty FullName)
$testSources += Join-Path $projectRoot 'Assets/AlterOS/Tests/Editor/Conversation/DirectConversationCoordinatorTests.cs'
$testSources += Join-Path $projectRoot 'Assets/AlterOS/Tests/Editor/Conversation/ConversationContractTests.cs'
$testDll = Join-Path $scratch 'AlterOS.Scenario.Checks.dll'
& dotnet $compiler /nologo /noconfig /nostdlib+ /target:library "/out:$testDll" $refs "/reference:$runtimeDll" "/reference:$nunit" $testSources
if ($LASTEXITCODE -ne 0) { throw 'Scenario test source compilation failed.' }
$testAssembly = [Reflection.Assembly]::LoadFrom($testDll)
$nativeRoot = Join-Path $scratch 'native'
New-Item -ItemType Directory -Path $nativeRoot -Force | Out-Null
Copy-Item -LiteralPath $nativePlugin -Destination $nativeRoot
$oldPath = $env:PATH
$env:PATH = "$nativeRoot;$oldPath"
$passed = 0; $failed = 0
try {
    foreach ($type in $testAssembly.GetTypes() | Where-Object { $_.Name -like '*Tests' -and $_.Name -like "*$Filter*" }) {
        foreach ($method in $type.GetMethods() | Where-Object { $_.GetCustomAttributes($false) | Where-Object { $_.GetType().Name -eq 'TestAttribute' } }) {
            try { $method.Invoke([Activator]::CreateInstance($type), @()) | Out-Null; $passed++; "PASS $($type.Name).$($method.Name)" }
            catch { $failed++; $detail = $_.Exception; while ($detail.InnerException) { $detail = $detail.InnerException }; "FAIL $($type.Name).$($method.Name): $($detail.Message)" }
        }
    }
    "Scenario source checks: $passed passed, $failed failed. Artifacts: $scratch"
    if (($passed + $failed) -eq 0) { throw 'No Scenario checks discovered.' }
    if ($failed) { exit 1 }
}
finally { $env:PATH = $oldPath }
