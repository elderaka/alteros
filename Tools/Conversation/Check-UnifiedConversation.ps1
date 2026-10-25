param([string]$Filter = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$deferredTestSource = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'Assets/AlterOS/Tests/Editor/Conversation/DirectConversationCoordinatorTests.cs'))
$deferredStart = $deferredTestSource.IndexOf('public async Task ConsentRevokedDuringProviderWaitDiscardsMigratedHistoryReply()', [System.StringComparison]::Ordinal)
$deferredTestsEnd = $deferredTestSource.IndexOf('public void MigratedHistoryDoesNotEnableOnlineGenerationAndRemainsInChatHistory()', $deferredStart, [System.StringComparison]::Ordinal)
$deferredHelperStart = $deferredTestSource.IndexOf('private static async Task MigratedTranscriptHistoryIsNotReplayedAndPendingReplyIsRejectedAfterMemoryPolicyChanges(', [System.StringComparison]::Ordinal)
$deferredHelperEnd = $deferredTestSource.IndexOf('public void UnavailableGteIsReportedAsAnExplicitLocalFailure()', $deferredHelperStart, [System.StringComparison]::Ordinal)
if ($deferredStart -lt 0 -or $deferredTestsEnd -le $deferredStart -or $deferredHelperStart -lt 0 -or $deferredHelperEnd -le $deferredHelperStart) {
    throw 'The deferred provider regression methods are missing or not async.'
}
$deferredTestBlock = $deferredTestSource.Substring($deferredStart, $deferredTestsEnd - $deferredStart) +
    $deferredTestSource.Substring($deferredHelperStart, $deferredHelperEnd - $deferredHelperStart)
if ($deferredTestBlock -match 'SpinWait\.SpinUntil|\.GetAwaiter\(\)\.GetResult\(\)') {
    throw 'The deferred provider regressions must not synchronously block the Unity test thread.'
}
$scratch = Join-Path $projectRoot ('.superpowers/sdd/Unified Conversation Implementation Plan/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$compiler = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\DotNetSdkRoslyn\csc.dll'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Unity bundled Roslyn compiler not found.' }
$refs = @('mscorlib.dll','System.dll','System.Core.dll','Microsoft.CSharp.dll','System.Runtime.Serialization.dll','System.Xml.dll') |
    ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$unityCore = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\Managed\UnityEngine\UnityEngine.CoreModule.dll'
$unityJson = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\Managed\UnityEngine\UnityEngine.JSONSerializeModule.dll'
$newtonsoft = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' |
    ForEach-Object { Join-Path $_.FullName 'Runtime/Newtonsoft.Json.dll' } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
$facade = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\MonoBleedingEdge\lib\mono\4.5\Facades\netstandard.dll'
$scriptAssemblies = Join-Path $projectRoot 'Library/ScriptAssemblies'
$sqlitePackage = Join-Path $projectRoot 'Library/PackageCache/com.gilzoide.sqlite-net@08248bd5884d'
$sqliteManaged = Join-Path $scriptAssemblies 'Gilzoide.SqliteNet.dll'
$unityCollections = Join-Path $scriptAssemblies 'Unity.Collections.dll'
$nativePlugin = Join-Path $sqlitePackage 'Plugins/lib/windows/x86_64/gilzoide-sqlite-net.dll'
foreach ($dependency in @($sqliteManaged, $unityCollections, $nativePlugin)) {
    if (!(Test-Path -LiteralPath $dependency -PathType Leaf)) { throw "Transcript SQLite dependency is missing: $dependency" }
}
$refs += @("/reference:$unityCore", "/reference:$unityJson", "/reference:$newtonsoft", "/reference:$facade", "/reference:$sqliteManaged", "/reference:$unityCollections")
[Reflection.Assembly]::LoadFrom($unityCore) | Out-Null
[Reflection.Assembly]::LoadFrom($unityJson) | Out-Null
[Reflection.Assembly]::LoadFrom($newtonsoft) | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Stats') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += @('DialogueDefinition.cs','DialogueValidator.cs','CanonicalTimeWindow.cs','CanonicalDialogueEligibilityEvaluator.cs') |
    ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Dialogue/' + $_) }
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Topics') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += @('TopicRoutePolicy.cs','DialogueRouteIndex.cs','TopicFamilySemanticMatcher.cs') |
    ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Semantics/' + $_) }
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Content') -Filter '*.cs' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Authoring') -Filter '*.cs' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Recording') -Filter '*.cs' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Conversation') -Filter '*.cs' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Memory') -Filter '*.cs' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
$scenarioRoot = Join-Path $projectRoot 'Assets/AlterOS/Scenarios'
if (Test-Path -LiteralPath $scenarioRoot) {
    $sources += @(Get-ChildItem -LiteralPath $scenarioRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName)
}
$runtimeDll = Join-Path $scratch 'AlterOS.Conversation.Runtime.dll'
& dotnet $compiler /nologo /noconfig /nostdlib+ /target:library "/out:$runtimeDll" $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Conversation runtime source compilation failed.' }
$netstandardRuntime = Join-Path $scratch 'netstandard.dll'
Copy-Item -LiteralPath $facade -Destination $netstandardRuntime
[Reflection.Assembly]::LoadFrom($netstandardRuntime) | Out-Null
[Reflection.Assembly]::LoadFrom($sqliteManaged) | Out-Null
[Reflection.Assembly]::LoadFrom($unityCollections) | Out-Null
[Reflection.Assembly]::LoadFrom($runtimeDll) | Out-Null
$nunit = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.ext.nunit@*' |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -Filter nunit.framework.dll } | Select-Object -First 1 -ExpandProperty FullName
if (!$nunit) { throw 'Installed NUnit not found.' }
[Reflection.Assembly]::LoadFrom($nunit) | Out-Null
$testSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Tests/Editor/Conversation') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$testSources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Tests/Editor/Memory') -Filter '*.cs' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
$scenarioTests = Join-Path $projectRoot 'Assets/AlterOS/Tests/Editor/Scenarios'
if (Test-Path -LiteralPath $scenarioTests) {
    $testSources += @(Get-ChildItem -LiteralPath $scenarioTests -Filter '*.cs' | Select-Object -ExpandProperty FullName)
}
$testDll = Join-Path $scratch 'AlterOS.Conversation.Checks.dll'
& dotnet $compiler /nologo /noconfig /nostdlib+ /target:library "/out:$testDll" $refs "/reference:$runtimeDll" "/reference:$nunit" $testSources
if ($LASTEXITCODE -ne 0) { throw 'Conversation test source compilation failed.' }
$testAssembly = [Reflection.Assembly]::LoadFrom($testDll)
$nativeRoot = Join-Path $scratch 'native'
New-Item -ItemType Directory -Path $nativeRoot -Force | Out-Null
Copy-Item -LiteralPath $nativePlugin -Destination $nativeRoot
$oldPath = $env:PATH
$env:PATH = "$nativeRoot;$oldPath"
$passed = 0; $failed = 0
foreach ($type in $testAssembly.GetTypes() | Where-Object { $_.Name -like '*Tests' -and $_.Name -like "*$Filter*" }) {
    foreach ($method in $type.GetMethods() | Where-Object { $_.GetCustomAttributes($false) | Where-Object { $_.GetType().Name -eq 'TestAttribute' } }) {
        try {
            $previousSynchronizationContext = [System.Threading.SynchronizationContext]::Current
            try {
                [System.Threading.SynchronizationContext]::SetSynchronizationContext($null)
                $testResult = $method.Invoke([Activator]::CreateInstance($type), @())
                if ($testResult -is [System.Threading.Tasks.Task]) { $testResult.GetAwaiter().GetResult() }
            } finally {
                [System.Threading.SynchronizationContext]::SetSynchronizationContext($previousSynchronizationContext)
            }
            $passed++; "PASS $($type.Name).$($method.Name)"
        }
        catch { $failed++; $detail = $_.Exception; while ($detail.InnerException) { $detail = $detail.InnerException }; "FAIL $($type.Name).$($method.Name): $($detail.Message)" }
    }
}
"Unified conversation source checks: $passed passed, $failed failed. Artifacts: $scratch"
if (($passed + $failed) -eq 0) { $env:PATH = $oldPath; throw 'No conversation checks discovered.' }
$env:PATH = $oldPath
if ($failed) { exit 1 }
