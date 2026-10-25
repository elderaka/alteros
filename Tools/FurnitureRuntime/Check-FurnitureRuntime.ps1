param([string]$Filter = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scratch = Join-Path $projectRoot ('.superpowers/sdd/Furniture Inventory and Placement Implementation Plan/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$compiler = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\DotNetSdkRoslyn\csc.dll'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$netstandard = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\MonoBleedingEdge\lib\mono\4.7.2-api\Facades\netstandard.dll'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Unity bundled Roslyn compiler not found.' }
if (!(Test-Path -LiteralPath $netstandard)) { throw 'Unity bundled .NET Standard reference assembly not found.' }
$refs = @('mscorlib.dll','System.dll','System.Core.dll','Microsoft.CSharp.dll','System.Runtime.Serialization.dll','System.Xml.dll') |
    ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$newtonsoft = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -Filter Newtonsoft.Json.dll | Select-Object -First 1 -ExpandProperty FullName } |
    Select-Object -First 1
if (!$newtonsoft) { throw 'Installed Newtonsoft.Json assembly not found.' }
$refs += '/reference:' + $newtonsoft
$refs += '/reference:' + $netstandard
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Furniture') -Filter '*.cs' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Debugging') -Filter '*.cs' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
$sources += @(
    (Join-Path $projectRoot 'Assets/AlterOS/Content/ContentContracts.cs'),
    (Join-Path $projectRoot 'Assets/AlterOS/Content/ContentBuildManifest.cs'),
    (Join-Path $projectRoot 'Assets/AlterOS/Content/ContentJson.cs'))
$runtimeDll = Join-Path $scratch 'AlterOS.Furniture.dll'
if ($sources.Count -gt 0) {
    & dotnet $compiler /nologo /noconfig /nostdlib+ /target:library "/out:$runtimeDll" $refs $sources
    if ($LASTEXITCODE -ne 0) { throw 'Furniture portable source compilation failed (not a test result).' }
}
$nunit = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.ext.nunit@*' |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -Filter nunit.framework.dll } | Select-Object -First 1 -ExpandProperty FullName
if (!$nunit) { throw 'Installed NUnit not found.' }
$portableTestNames = @(
    'FurnitureCatalogueTests.cs','FurnitureArrangementTestData.cs','FurnitureArrangementTests.cs',
    'FurnitureChoiceTests.cs','FurnitureReservationTests.cs','FurnitureLayoutTestData.cs',
    'FurnitureLayoutTests.cs','HomeFurnitureRepositoryTests.cs','FurnitureInteractionTests.cs',
    'FurnitureInventoryTests.cs','FurnitureHandlingTests.cs','FurnitureInputPolicyTests.cs','DebugModeStateTests.cs',
    'FurnitureSceneAdapterEditorTests.cs','FurnitureFunctionTests.cs','FurniturePresentationTests.cs',
    'FurniturePowerStateProjectionTests.cs')
$tests = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Tests/Editor/Furniture') -Filter '*.cs' -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in $portableTestNames } | Select-Object -ExpandProperty FullName)
if ($tests.Count -eq 0) { throw 'No Furniture tests found.' }
$testDll = Join-Path $scratch 'FurnitureRuntime.Checks.dll'
$testRefs = @($refs + "/reference:$nunit")
if (Test-Path -LiteralPath $runtimeDll) { $testRefs += "/reference:$runtimeDll" }
& dotnet $compiler /nologo /noconfig /nostdlib+ /target:library "/out:$testDll" $testRefs $tests
if ($LASTEXITCODE -ne 0) { throw 'Furniture test source compilation failed (not a test result).' }
[Reflection.Assembly]::LoadFrom($nunit) | Out-Null
if (Test-Path -LiteralPath $runtimeDll) { [Reflection.Assembly]::LoadFrom($runtimeDll) | Out-Null }
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
"Furniture source checks: $passed passed, $failed failed. Artifacts: $scratch"
if (($passed + $failed) -eq 0) { throw 'No matching furniture tests discovered.' }
if ($failed) { exit 1 }
if (Test-Path -LiteralPath $runtimeDll) {
    $runtime = [Reflection.Assembly]::LoadFrom($runtimeDll)
    $unityRefs = @($runtime.GetReferencedAssemblies() | Where-Object { $_.Name -like 'UnityEngine*' -or $_.Name -like 'UnityEditor*' })
    if ($unityRefs.Count) { throw ('Portable furniture core has Unity references: ' + (($unityRefs | ForEach-Object Name) -join ', ')) }
    'PASS Furniture portable output has no UnityEngine/UnityEditor references.'
}
