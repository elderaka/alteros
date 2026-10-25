param(
    [string]$CataloguePath = (Join-Path $PSScriptRoot 'StagedCatalogue')
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$catalogueRoot = [System.IO.Path]::GetFullPath($CataloguePath)
if (!(Test-Path -LiteralPath $catalogueRoot -PathType Container)) {
    throw "Catalogue directory not found: $catalogueRoot"
}

$compiler = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\DotNetSdkRoslyn\csc.dll'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$unityCore = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\Managed\UnityEngine\UnityEngine.CoreModule.dll'
$facade = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\MonoBleedingEdge\lib\mono\4.5\Facades\netstandard.dll'
foreach ($required in @($compiler, $unityCore, $facade)) {
    if (!(Test-Path -LiteralPath $required)) { throw "Required Unity build reference not found: $required" }
}

$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$scratch = Join-Path $tempRoot ('AlterOSTopicValidation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $references = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Runtime.Serialization.dll', 'System.Xml.dll') |
        ForEach-Object { '/reference:' + (Join-Path $framework $_) }
    $references += @("/reference:$unityCore", "/reference:$facade")
    [Reflection.Assembly]::LoadFrom($unityCore) | Out-Null

    $sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Stats') -Filter '*.cs' |
        Select-Object -ExpandProperty FullName)
    $sources += @('DialogueDefinition.cs', 'DialogueValidator.cs', 'CanonicalTimeWindow.cs', 'CanonicalDialogueEligibilityEvaluator.cs') |
        ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Dialogue/' + $_) }
    $sources += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Topics') -Filter '*.cs' |
        Select-Object -ExpandProperty FullName
    $sources += Join-Path $PSScriptRoot 'TopicCatalogueValidator.cs'

    $runtimeDll = Join-Path $scratch 'AlterOS.TopicCatalogueValidation.dll'
    & dotnet $compiler /nologo /noconfig /nostdlib+ /target:library "/out:$runtimeDll" $references $sources
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile the production Topic validator sources.' }

    # Load compiled bytes so the temporary DLL is not locked when the owned temp folder is removed.
    $assembly = [Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($runtimeDll))
    $type = $assembly.GetType('AlterOS.Tools.TopicCatalogueValidator', $true)
    $result = $type.GetMethod('ValidateDirectory').Invoke($null, @($catalogueRoot))
    $result
    if (!$result.StartsWith('PASS:', [StringComparison]::Ordinal)) { exit 1 }
}
finally {
    $resolvedScratch = [System.IO.Path]::GetFullPath($scratch)
    if (!$resolvedScratch.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a validation directory outside the system temp folder.'
    }
    Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
}
