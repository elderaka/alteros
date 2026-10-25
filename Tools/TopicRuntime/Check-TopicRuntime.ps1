param([string]$Filter = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scratch = Join-Path $projectRoot ('.superpowers/sdd/Topic Runtime Implementation Plan/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$compiler = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\DotNetSdkRoslyn\csc.dll'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Unity bundled Roslyn compiler not found; update the explicit local compiler path.' }
$refs = @('mscorlib.dll','System.dll','System.Core.dll','Microsoft.CSharp.dll','System.Runtime.Serialization.dll','System.Xml.dll') |
    ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$unityCore = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\Managed\UnityEngine\UnityEngine.CoreModule.dll'
$facade = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\MonoBleedingEdge\lib\mono\4.5\Facades\netstandard.dll'
$refs += @("/reference:$unityCore", "/reference:$facade")
[Reflection.Assembly]::LoadFrom($unityCore) | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Stats') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += @('DialogueDefinition.cs','DialogueValidator.cs','CanonicalTimeWindow.cs','CanonicalDialogueEligibilityEvaluator.cs') |
    ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Dialogue/' + $_) }
$topicRoot = Join-Path $projectRoot 'Assets/AlterOS/Topics'
if (Test-Path -LiteralPath $topicRoot) { $sources += Get-ChildItem -LiteralPath $topicRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName }
$sources += Join-Path $PSScriptRoot 'TopicCatalogueValidator.cs'
$sources += Join-Path $PSScriptRoot 'TopicContentRefShim.cs'
$sources += Join-Path $projectRoot 'Assets/AlterOS/Scenarios/ScenarioHistoryObservation.cs'
$runtimeDll = Join-Path $scratch 'AlterOS.Runtime.dll'
& dotnet $compiler /nologo /noconfig /nostdlib+ /target:library "/out:$runtimeDll" $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Runtime source compilation failed (not a test result).' }
$netstandardRuntime = Join-Path $scratch 'netstandard.dll'
Copy-Item -LiteralPath $facade -Destination $netstandardRuntime
[Reflection.Assembly]::LoadFrom($netstandardRuntime) | Out-Null
[Reflection.Assembly]::LoadFrom($runtimeDll) | Out-Null
$nunit = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.ext.nunit@*' |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -Filter nunit.framework.dll } | Select-Object -First 1 -ExpandProperty FullName
if (!$nunit) { throw 'Installed NUnit not found.' }
[Reflection.Assembly]::LoadFrom($nunit) | Out-Null
$ownedTests = @(
    'TopicCatalogueJsonTests.cs', 'TopicConditionTests.cs', 'TopicDefinitionTests.cs', 'TopicTestData.cs',
    'TopicHistoryRepositoryTests.cs', 'TopicHistoryTests.cs', 'TopicScenarioSelectionTests.cs')
$tests = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Tests/Editor/TopicRuntime') -Filter '*.cs' |
    Where-Object { $_.Name -in $ownedTests } | Select-Object -ExpandProperty FullName)
$testDll = Join-Path $scratch 'TopicRuntime.Checks.dll'
& dotnet $compiler /nologo /noconfig /nostdlib+ /target:library "/out:$testDll" $refs "/reference:$runtimeDll" "/reference:$nunit" $tests
if ($LASTEXITCODE -ne 0) { throw 'Test source compilation failed (not a test result).' }
$testAssembly = [Reflection.Assembly]::LoadFrom($testDll)
$passed = 0; $failed = 0
foreach ($type in $testAssembly.GetTypes() | Where-Object { $_.Name -like '*Tests' -and $_.Name -like "*$Filter*" }) {
    foreach ($method in $type.GetMethods() | Where-Object { $_.GetCustomAttributes($false) | Where-Object { $_.GetType().Name -eq 'TestAttribute' } }) {
        try {
            $method.Invoke([Activator]::CreateInstance($type), @()) | Out-Null
            $passed++; "PASS $($type.Name).$($method.Name)"
        } catch {
            $failed++; $errorDetail = $_.Exception
            while ($errorDetail.InnerException) { $errorDetail = $errorDetail.InnerException }
            "FAIL $($type.Name).$($method.Name): $($errorDetail.Message)"
        }
    }
}
"Topic source checks: $passed passed, $failed failed. Artifacts: $scratch"
if (($passed + $failed) -eq 0) { throw 'No matching tests discovered.' }
if ($failed) { exit 1 }
