param(
    [string]$Output = "artifacts/local/scheduling-research/qualified",
    [string]$Fixtures = "artifacts/local/kernel-comparison/fixtures",
    [string]$Python = "artifacts/local/kernel-comparison/venv/Scripts/python.exe",
    [ValidateRange(1, 5)][int]$Rounds = 3
)

$ErrorActionPreference = "Stop"
Set-Location (Resolve-Path "$PSScriptRoot/../..").Path
if (Test-Path -LiteralPath $Output) {
    throw "Research output must be fresh: $Output"
}
New-Item -ItemType Directory -Path $Output | Out-Null
$outputPath = (Resolve-Path $Output).Path

function Invoke-Checked([string]$Executable, [string[]]$Arguments, [string]$Log) {
    & $Executable @Arguments *> $Log
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $Log -Tail 25
        throw "$Executable failed with exit code $LASTEXITCODE. See $Log"
    }
}

Invoke-Checked "dotnet" @("build", "tools/Aurelian.KernelComparison", "-c", "Release", "-m:1",
    "-nr:false", "-p:UseSharedCompilation=false") "$outputPath/build.log"
$protocol = @{
    Accepted = $false
    Commit = (& git rev-parse HEAD).Trim()
    Rounds = $Rounds
    ClockPolicy = "Unmanaged clocks; alternating backend order; no CUDA clock refresh; 10-second idle before each process."
    Sources = @{}
}
foreach ($source in @($PSCommandPath, "$PSScriptRoot/cuda_research.py", "$PSScriptRoot/cuda_profile.py",
    "$PSScriptRoot/summarize_research.py")) {
    $protocol.Sources[(Resolve-Path $source).Path] = (Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant()
}
$protocolPath = "$outputPath/protocol.json"
$protocol | ConvertTo-Json -Depth 8 | Set-Content $protocolPath
$dll = "tools/Aurelian.KernelComparison/bin/Release/net10.0/Aurelian.KernelComparison.dll"
Invoke-Checked "dotnet" @($dll, "--research", $Fixtures, "$outputPath/validation", "--validation") "$outputPath/validation.log"
if (Select-String -Path "$outputPath/validation.log" -Pattern "Validation Error|Validation Warning" -Quiet) {
    throw "Vulkan validation emitted diagnostics. See validation.log"
}
for ($round = 1; $round -le $Rounds; $round++) {
    $roundPath = "$outputPath/run$round"
    New-Item -ItemType Directory $roundPath | Out-Null
    $modes = if ($round % 2 -eq 1) { @("vulkan", "cuda") } else { @("cuda", "vulkan") }
    foreach ($mode in $modes) {
        Write-Host "Research round $round/$Rounds`: $mode"
        Start-Sleep -Seconds 10
        if ($mode -eq "vulkan") {
            Invoke-Checked "dotnet" @($dll, "--research", $Fixtures, "$roundPath/vulkan") "$roundPath/vulkan.log"
        }
        else {
            Invoke-Checked $Python @("tools/kernel-comparison/cuda_research.py", $Fixtures, "$roundPath/cuda") "$roundPath/cuda.log"
        }
    }
}
Invoke-Checked $Python @("tools/kernel-comparison/summarize_research.py", $outputPath) "$outputPath/RESULTS.txt"
foreach ($source in $protocol.Sources.Keys) {
    if ((Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $protocol.Sources[$source]) {
        throw "Research source changed: $source"
    }
}
$protocol.Accepted = $true
$protocol | ConvertTo-Json -Depth 8 | Set-Content $protocolPath
Get-Content "$outputPath/RESULTS.txt"
