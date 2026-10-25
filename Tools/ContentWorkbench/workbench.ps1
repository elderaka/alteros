$ErrorActionPreference = 'Stop'
$hostPath = Join-Path $PSScriptRoot 'CoreHost/bin/AlterOS.ContentWorkbench.exe'
if (!(Test-Path -LiteralPath $hostPath)) {
    [Console]::Error.WriteLine('Workbench host is not built. Run build-core.ps1 first.')
    exit 1
}

$maximumBytes = 1024 * 1024
$inputBytes = New-Object System.IO.MemoryStream
$inputStream = [Console]::OpenStandardInput()
$buffer = New-Object byte[] 8192
while (($read = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
    $remaining = ($maximumBytes + 1) - [int]$inputBytes.Length
    if ($remaining -gt 0) {
        $copy = [Math]::Min($read, $remaining)
        $inputBytes.Write($buffer, 0, $copy)
    }
}

$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = $hostPath
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $false
$child = New-Object System.Diagnostics.Process
$child.StartInfo = $start
if (!$child.Start()) {
    [Console]::Error.WriteLine('Workbench host could not be started.')
    exit 1
}

$inputBytes.Position = 0
$inputBytes.CopyTo($child.StandardInput.BaseStream)
$child.StandardInput.Close()
$outputBytes = New-Object System.IO.MemoryStream
$child.StandardOutput.BaseStream.CopyTo($outputBytes)
$child.WaitForExit()
$response = $outputBytes.ToArray()
[Console]::OpenStandardOutput().Write($response, 0, $response.Length)
exit $child.ExitCode
