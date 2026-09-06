param(
    [int]$TimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "samples/Integrations/Aurelian.Ariadne.VnDemo/Aurelian.Ariadne.VnDemo.csproj"
$executablePath = Join-Path $repositoryRoot "samples/Integrations/Aurelian.Ariadne.VnDemo/bin/Debug/net10.0/Aurelian.Ariadne.VnDemo.exe"
$existingMsBuildProcesses = @(
    Get-Process -Name MSBuild -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Id
)

$previousNodeReuse = $env:MSBUILDDISABLENODEREUSE
$env:MSBUILDDISABLENODEREUSE = "1"
try {
    & dotnet build $projectPath --nologo --verbosity quiet -m:1 -nodeReuse:false -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) {
        throw "The SUNKILL smoke-test build failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:MSBUILDDISABLENODEREUSE = $previousNodeReuse
}

if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "The SUNKILL executable was not produced at '$executablePath'."
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $executablePath
$startInfo.ArgumentList.Add("--launch-smoke")
$startInfo.WorkingDirectory = Split-Path -Parent $executablePath
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
if (-not $process.Start()) {
    throw "The SUNKILL smoke-test process did not start."
}

$stdoutTask = $process.StandardOutput.ReadToEndAsync()
$stderrTask = $process.StandardError.ReadToEndAsync()
$timeoutMilliseconds = $TimeoutSeconds * 1000
if (-not $process.WaitForExit($timeoutMilliseconds)) {
    $process.Kill($true)
    $process.WaitForExit()
    $timedOutStdout = $stdoutTask.GetAwaiter().GetResult()
    $timedOutStderr = $stderrTask.GetAwaiter().GetResult()
    throw "SUNKILL did not open and render within $TimeoutSeconds seconds. The child process tree was terminated.`nSTDOUT:`n$timedOutStdout`nSTDERR:`n$timedOutStderr"
}

$stdout = $stdoutTask.GetAwaiter().GetResult()
$stderr = $stderrTask.GetAwaiter().GetResult()
if ($process.ExitCode -ne 0) {
    throw "SUNKILL launch smoke failed with exit code $($process.ExitCode).`nSTDOUT:`n$stdout`nSTDERR:`n$stderr"
}

if (-not $stdout.Contains("SUNKILL_LAUNCH_READY", [StringComparison]::Ordinal)) {
    throw "SUNKILL exited without reporting a rendered, opened window.`nSTDOUT:`n$stdout`nSTDERR:`n$stderr"
}

Start-Sleep -Milliseconds 500
$newMsBuildProcesses = @(
    Get-Process -Name MSBuild -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -notin $existingMsBuildProcesses }
)
if ($newMsBuildProcesses.Count -ne 0) {
    $processList = $newMsBuildProcesses |
        ForEach-Object { "$($_.Id):$($_.ProcessName)" }
    throw "The launch smoke left new reusable MSBuild processes behind: $($processList -join ', ')."
}

Write-Output "SUNKILL launch smoke passed: native window opened, rendered, and exited cleanly."
Write-Output "No new reusable MSBuild process remained after the test."
