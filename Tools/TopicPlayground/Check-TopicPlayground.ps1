param([string]$Filter = 'TopicPlaygroundFamilyTests')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent | Split-Path -Parent
Set-Location -LiteralPath $projectRoot
$scratch = Join-Path $projectRoot ('.superpowers/sdd/Topic Playground Integration/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$compiler = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\DotNetSdkRoslyn\csc.dll'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Unity bundled Roslyn compiler not found; update the explicit local compiler path.' }
$refs = @('mscorlib.dll','System.dll','System.Core.dll','Microsoft.CSharp.dll','System.Runtime.Serialization.dll','System.Xml.dll') |
    ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$unityCore = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\Managed\UnityEngine\UnityEngine.CoreModule.dll'
$unityJson = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\Managed\UnityEngine\UnityEngine.JSONSerializeModule.dll'
$unityImgui = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\Managed\UnityEngine\UnityEngine.IMGUIModule.dll'
$facade = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\MonoBleedingEdge\lib\mono\4.5\Facades\netstandard.dll'
$newtonsoft = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' |
    ForEach-Object { Join-Path $_.FullName 'Runtime/Newtonsoft.Json.dll' } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
$sqliteManaged = Join-Path $projectRoot 'Library/ScriptAssemblies/Gilzoide.SqliteNet.dll'
$unityCollections = Join-Path $projectRoot 'Library/ScriptAssemblies/Unity.Collections.dll'
$inference = Join-Path $projectRoot 'Library/ScriptAssemblies/Unity.InferenceEngine.dll'
$tokenization = Join-Path $projectRoot 'Library/ScriptAssemblies/Unity.InferenceEngine.Tokenization.dll'
$refs += @("/reference:$unityCore", "/reference:$unityJson", "/reference:$unityImgui", "/reference:$facade", "/reference:$newtonsoft",
    "/reference:$sqliteManaged", "/reference:$unityCollections", "/reference:$inference", "/reference:$tokenization")
[Reflection.Assembly]::LoadFrom($unityCore) | Out-Null
[Reflection.Assembly]::LoadFrom($unityJson) | Out-Null
[Reflection.Assembly]::LoadFrom($newtonsoft) | Out-Null
$sources = @()
foreach ($folder in @('Stats','Dialogue','Topics')) {
    $sources += Get-ChildItem -LiteralPath (Join-Path $projectRoot ('Assets/AlterOS/' + $folder)) -Filter '*.cs' |
        Select-Object -ExpandProperty FullName
}
$sources += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Content') -Filter '*.cs' |
    Select-Object -ExpandProperty FullName
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Conversation') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Memory') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Recording') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Scenarios') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += @('DialogueRouteIndex.cs','TopicRoutePolicy.cs','TopicFamilySemanticMatcher.cs','GteSemanticQueryEncoder.cs') |
    ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Semantics/' + $_) }
$runtimeDll = Join-Path $scratch 'AlterOS.Playground.Runtime.dll'
& dotnet $compiler /nologo /noconfig /nostdlib+ /target:library "/out:$runtimeDll" $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Playground source compilation failed (not a test result).' }
[Reflection.Assembly]::LoadFrom($newtonsoft) | Out-Null
[Reflection.Assembly]::LoadFrom($runtimeDll) | Out-Null
$editorRefs = @($refs) + @('/reference:' + $runtimeDll)
$editorManaged = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\Managed\UnityEngine'
$editorRefs += Get-ChildItem -LiteralPath $editorManaged -Filter 'UnityEditor*.dll' |
    ForEach-Object { '/reference:' + $_.FullName }
$editorSource = @('TopicPlaygroundSession.cs','TopicPlaygroundWindow.cs','TopicPlaygroundCommand.cs','TopicPlaygroundModelFingerprint.cs') |
    ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Editor/' + $_) }
$windowSourceText = Get-Content -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Editor/TopicPlaygroundWindow.cs') -Raw
if ($windowSourceText -notmatch 'EditorGUILayout\.DoubleField\("Deterministic scenario draw') {
    throw 'The Editor must preserve the supplied double-precision scenario draw.'
}
if ($windowSourceText -match 'EditorGUILayout\.Slider\("Deterministic scenario draw') {
    throw 'The scenario draw must not be rounded or clamped by an Editor slider.'
}
$editorDll = Join-Path $scratch 'AlterOS.Playground.Editor.dll'
& dotnet $compiler /nologo /noconfig /nostdlib+ /target:library "/out:$editorDll" $editorRefs $editorSource
if ($LASTEXITCODE -ne 0) { throw 'Playground Editor source compilation failed (not a Unity import result).' }
$nunit = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.ext.nunit@*' |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -Filter nunit.framework.dll } |
    Select-Object -First 1 -ExpandProperty FullName
if (!$nunit) { throw 'Installed NUnit not found.' }
[Reflection.Assembly]::LoadFrom($nunit) | Out-Null
$testSources = @(Join-Path $projectRoot 'Assets/AlterOS/Tests/Editor/TopicRuntime/TopicPlaygroundFamilyTests.cs')
$testSources += @(Join-Path $PSScriptRoot 'TopicPlaygroundModelFingerprintTests.cs')
$testSources += @(Join-Path $projectRoot 'Assets/AlterOS/Editor/TopicPlaygroundModelFingerprint.cs')
$testSources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Tests/Editor/Scenarios') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$testDll = Join-Path $scratch 'TopicPlayground.Checks.dll'
& dotnet $compiler /nologo /noconfig /nostdlib+ /target:library "/out:$testDll" $refs "/reference:$runtimeDll" "/reference:$nunit" $testSources
if ($LASTEXITCODE -ne 0) { throw 'Playground test source compilation failed (not a test result).' }
$testAssembly = [Reflection.Assembly]::LoadFrom($testDll)
$passed = 0; $failed = 0
 $originalProcessDirectory = [Environment]::CurrentDirectory
try {
    [Environment]::CurrentDirectory = $projectRoot
    foreach ($type in $testAssembly.GetTypes() | Where-Object { $_.Name -like '*Tests' -and $_.Name -like "*$Filter*" }) {
        foreach ($method in $type.GetMethods() | Where-Object { $_.GetCustomAttributes($false) | Where-Object { $_.GetType().Name -eq 'TestAttribute' } }) {
            try {
                $method.Invoke([Activator]::CreateInstance($type), @()) | Out-Null
                $passed++; "PASS $($type.Name).$($method.Name)"
            } catch {
                $failed++; $failure = $_.Exception
                while ($failure.InnerException) { $failure = $failure.InnerException }
                "FAIL $($type.Name).$($method.Name): $($failure.Message)"
            }
        }
    }
}
finally {
    [Environment]::CurrentDirectory = $originalProcessDirectory
}
"Topic Playground checks: $passed passed, $failed failed. Artifacts: $scratch"
"Editor scenario draw precision source check: PASS"
if (($passed + $failed) -eq 0) { throw 'No matching tests discovered.' }
if ($failed) { exit 1 }
