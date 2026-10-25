param([Parameter(Mandatory=$true)][string]$JsonPath)
$ErrorActionPreference = 'Stop'
$projectRoot = 'E:/Pleiades/alteros'
$scratch = Join-Path $PSScriptRoot ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$compiler = 'C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Data/DotNetSdkRoslyn/csc.dll'
$framework = 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319'
$refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.Xml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$core = 'C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
$facade = 'C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Data/MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'
$refs += @("/reference:$core", "/reference:$facade")
[Reflection.Assembly]::LoadFrom($core) | Out-Null
$sources = @('StatCatalogue.cs','StatSnapshot.cs','CanonicalStatEligibility.cs') | ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Stats/' + $_) }
$sources += @('DialogueDefinition.cs','DialogueValidator.cs','CanonicalTimeWindow.cs','CanonicalDialogueEligibilityEvaluator.cs') | ForEach-Object { Join-Path $projectRoot ('Assets/AlterOS/Dialogue/' + $_) }
$sources += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/AlterOS/Topics') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$sources += Join-Path $projectRoot 'Tools/TopicRuntime/TopicCatalogueValidator.cs'
$dll = Join-Path $scratch 'PromptReference.Check.dll'
& dotnet $compiler /nologo /noconfig /nostdlib+ /target:library "/out:$dll" $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Source compile failed; not a validation result.' }
$assembly = [Reflection.Assembly]::LoadFrom($dll)
$validator = $assembly.GetType('AlterOS.Tools.TopicCatalogueValidator')
$errors = $validator.GetMethod('ValidateFile').Invoke($null, @([IO.Path]::GetFullPath($JsonPath)))
if ($errors.Count) { $errors; exit 1 }
'PASS: actual TopicDefinitionValidator accepted reference JSON.'
