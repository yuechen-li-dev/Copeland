param(
    [string]$Output = "artifacts/local/vulkan-profile/qualified",
    [string]$Fixtures = "artifacts/local/kernel-comparison/fixtures",
    [string]$Python = "artifacts/local/kernel-comparison/venv/Scripts/python.exe",
    [ValidateRange(1, 5)][int]$Rounds = 3
)

$ErrorActionPreference = "Stop"
$repository = (Resolve-Path "$PSScriptRoot/../..").Path
Set-Location $repository
if (Test-Path -LiteralPath $Output) {
    throw "Profiling output must be fresh: $Output"
}
New-Item -ItemType Directory -Path $Output | Out-Null
$outputPath = (Resolve-Path $Output).Path

function Invoke-Checked([string]$Executable, [string[]]$Arguments, [string]$Log) {
    & $Executable @Arguments *> $Log
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $Log -Tail 20
        throw "$Executable failed with exit code $LASTEXITCODE. See $Log"
    }
}

Invoke-Checked "dotnet" @("build", "tools/Aurelian.KernelComparison", "-c", "Release", "-m:1",
    "-nr:false", "-p:UseSharedCompilation=false") "$outputPath/build.log"
$protocol = @{
    Accepted = $false
    Rounds = $Rounds
    Commit = (& git rev-parse HEAD).Trim()
    Python = (Resolve-Path $Python).Path
    Sources = @{}
    ClockControl = "Native Vulkan alone versus native Vulkan with a completed one-thread CUDA no-op before each compiler sample group, outside native timing regions; no clock settings changed."
    Cooldown = "20 seconds without benchmark activity before each pure Vulkan process; desktop remains active."
    NsightLimitation = "Local Nsight Systems Vulkan capture rejected registry registration: requires registry writing permissions."
    NvidiaSmi = (& nvidia-smi -q | Out-String)
}
foreach ($source in @($PSCommandPath, "$PSScriptRoot/cuda_profile.py", "$PSScriptRoot/summarize_profile.py")) {
    $protocol.Sources[(Resolve-Path $source).Path] = (Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant()
}
$protocolPath = "$outputPath/protocol.json"
$protocol | ConvertTo-Json -Depth 8 | Set-Content $protocolPath
$dll = "tools/Aurelian.KernelComparison/bin/Release/net10.0/Aurelian.KernelComparison.dll"

for ($round = 1; $round -le $Rounds; $round++) {
    $roundPath = "$outputPath/run$round"
    New-Item -ItemType Directory $roundPath | Out-Null
    $modes = if ($round % 2 -eq 1) { @("vulkan", "vulkan-cuda-context", "cuda") } else { @("cuda", "vulkan-cuda-context", "vulkan") }
    foreach ($mode in $modes) {
        Write-Host "Profiling round $round/$Rounds`: $mode"
        if ($mode -eq "cuda") {
            Invoke-Checked $Python @("tools/kernel-comparison/cuda_profile.py", $Fixtures, "$roundPath/cuda") "$roundPath/cuda.log"
        }
        else {
            if ($mode -eq "vulkan") {
                Start-Sleep -Seconds 20
            }
            $nativeArguments = @($dll, "--profile", $Fixtures, "$roundPath/$mode")
            if ($mode -eq "vulkan-cuda-context") {
                $nativeArguments += "--cuda-context"
            }
            Invoke-Checked "dotnet" $nativeArguments "$roundPath/$mode.log"
        }
    }
}
Invoke-Checked $Python @("tools/kernel-comparison/summarize_profile.py", $outputPath) "$outputPath/summary.log"
$protocol.Accepted = $true
$protocol | ConvertTo-Json -Depth 8 | Set-Content $protocolPath
Get-Content "$outputPath/RESULTS.md"
