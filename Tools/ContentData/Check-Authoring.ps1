param(
    [ValidateSet('Repository','Import','Interchange','All')]
    [string]$Group = 'Repository'
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unityVersionLine = Select-String -Path (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion:' | Select-Object -First 1
if ($null -eq $unityVersionLine) { throw 'Unity version was not found in ProjectVersion.txt.' }
$unityVersion = ($unityVersionLine.Line -split ':', 2)[1].Trim()
$unityData = Join-Path $env:ProgramFiles ("Unity\Hub\Editor\$unityVersion\Editor\Data")
$roslyn = Join-Path $unityData 'DotNetSdkRoslyn\csc.dll'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$frameworkCompiler = Join-Path $framework 'csc.exe'
$netstandard = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\Facades\netstandard.dll'
$sqlitePackage = Join-Path $projectRoot 'Library/PackageCache/com.gilzoide.sqlite-net@08248bd5884d'
$sqliteSource = Join-Path $sqlitePackage 'Runtime/sqlite-net/SQLite.cs'
$sqliteNative = Join-Path $sqlitePackage 'Plugins/lib/windows/x86_64/gilzoide-sqlite-net.dll'
$sqliteLicense = Join-Path $sqlitePackage 'Plugins/sqlite-net~/LICENSE.txt'
$packageLicense = Join-Path $sqlitePackage 'LICENSE.txt'
$newtonsoft = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' |
    ForEach-Object { Join-Path $_.FullName 'Runtime/Newtonsoft.Json.dll' } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1

foreach ($requiredPath in @($frameworkCompiler, $roslyn, $netstandard, $sqliteSource, $sqliteNative, $sqliteLicense, $packageLicense, $newtonsoft)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required local authoring dependency is missing: $requiredPath"
    }
}
if ([Environment]::Is64BitProcess -eq $false) { throw 'Run this check from 64-bit PowerShell for the Windows x64 SQLite binding.' }

$runRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('aos-authoring-' + [guid]::NewGuid().ToString('N'))
$buildRoot = Join-Path $runRoot 'build'
$deploymentRoot = Join-Path $runRoot 'deployment_日本語'
New-Item -ItemType Directory -Path $buildRoot,$deploymentRoot -Force | Out-Null
$succeeded = $false
$oldPath = $env:PATH
try {
    $authoringSources = @(Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Authoring') -Filter '*.cs' -File |
        Where-Object { $_.BaseName -ne 'AuthoringContentSourceProjection' -and $_.BaseName -ne 'ContentAuthoringValidator' } |
        ForEach-Object FullName)
    if ($authoringSources.Count -eq 0) { throw 'Authoring source files are missing.' }
    $runtimeOutput = Join-Path $buildRoot 'AlterOS.Authoring.dll'
    $references = @('mscorlib.dll','System.dll','System.Core.dll','Microsoft.CSharp.dll') |
        ForEach-Object { '/reference:' + (Join-Path $framework $_) }
    $references += "/reference:$netstandard"
    $references += "/reference:$newtonsoft"
    $standaloneShim = Join-Path $PSScriptRoot 'SqlitePreserveAttributeShim.cs'
    & dotnet $roslyn /nologo /noconfig /nostdlib+ /target:library "/out:$runtimeOutput" $references $standaloneShim $sqliteSource $authoringSources
    if ($LASTEXITCODE -ne 0) { throw "Standalone authoring library compilation failed with exit code $LASTEXITCODE. Artifacts: $runRoot" }

    $testOutput = Join-Path $buildRoot 'AuthoringRepositoryTests.exe'
    & $frameworkCompiler /nologo /platform:x64 /target:exe /reference:System.Core.dll "/out:$testOutput" (Join-Path $PSScriptRoot 'AuthoringRepositoryTests.cs')
    if ($LASTEXITCODE -ne 0) { throw "64-bit authoring test harness compilation failed with exit code $LASTEXITCODE. Artifacts: $runRoot" }

    Copy-Item -LiteralPath $runtimeOutput -Destination $deploymentRoot
    Copy-Item -LiteralPath $testOutput -Destination $deploymentRoot
    Copy-Item -LiteralPath $newtonsoft -Destination $deploymentRoot
    Copy-Item -LiteralPath $netstandard -Destination $deploymentRoot
    Copy-Item -LiteralPath $sqliteNative -Destination $deploymentRoot
    Copy-Item -LiteralPath $sqliteLicense -Destination (Join-Path $deploymentRoot 'sqlite-net-LICENSE.txt')
    Copy-Item -LiteralPath $packageLicense -Destination (Join-Path $deploymentRoot 'Gilzoide-SqliteNet-LICENSE.txt')

    $env:PATH = "$deploymentRoot;$oldPath"
    Push-Location $deploymentRoot
    try {
        & (Join-Path $deploymentRoot 'AuthoringRepositoryTests.exe') (Join-Path $deploymentRoot 'AlterOS.Authoring.dll') $Group
        if ($LASTEXITCODE -ne 0) { throw "Authoring $Group check failed with exit code $LASTEXITCODE. Artifacts: $runRoot" }
    }
    finally { Pop-Location }
    $succeeded = $true
}
finally {
    $env:PATH = $oldPath
    if ($succeeded) {
        $resolvedRunRoot = [System.IO.Path]::GetFullPath($runRoot)
        $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $resolvedRunRoot.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a validation directory outside the system temporary root: $resolvedRunRoot"
        }
        Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
    }
}
