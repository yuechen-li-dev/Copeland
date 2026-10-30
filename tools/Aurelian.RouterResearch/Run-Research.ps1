param(
    [string]$Output = "artifacts/local/router-research/qualified"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
Set-Location -LiteralPath $repoRoot
$outputPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $Output))
if (Test-Path -LiteralPath $outputPath) {
    throw "Research requires a fresh output directory: $outputPath"
}
New-Item -ItemType Directory -Path $outputPath | Out-Null

dotnet build tools/Aurelian.RouterResearch -c Release -m:1 -nr:false -p:UseSharedCompilation=false *> (Join-Path $outputPath "build.log")
if ($LASTEXITCODE -ne 0) {
    throw "Router research build failed; see build.log."
}

$metadata = [ordered]@{
    startedUtc = [DateTime]::UtcNow.ToString("o")
    gitHead = (& git rev-parse HEAD)
    gitStatus = (& git status --short)
    cpu = @(Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors)
    adapters = @(Get-NetAdapter | Select-Object Name, InterfaceDescription, LinkSpeed)
    dotnet = @(& dotnet --info)
    gpuBefore = @(& nvidia-smi --query-gpu=name,driver_version,pstate,clocks.sm,clocks.mem,power.draw,temperature.gpu --format=csv,noheader)
}
$metadata | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $outputPath "machine.json")

foreach ($name in @("validation", "round1", "round2", "round3")) {
    $runPath = Join-Path $outputPath $name
    $logPath = Join-Path $outputPath "$name.log"
    $runArguments = @("run", "--project", "tools/Aurelian.RouterResearch", "-c", "Release", "--no-build", "--", $runPath)
    if ($name -eq "validation") {
        $runArguments += "--validation"
    }
    Write-Host "Running $name..."
    & dotnet @runArguments *> $logPath
    if ($LASTEXITCODE -ne 0) {
        throw "$name failed; see $logPath."
    }
    if (Select-String -LiteralPath $logPath -Pattern "Validation Error|VUID-" -Quiet) {
        throw "$name reported Vulkan validation diagnostics; see $logPath."
    }
    Get-Content -LiteralPath $logPath -Tail 1
}

& nvidia-smi --query-gpu=name,driver_version,pstate,clocks.sm,clocks.mem,power.draw,temperature.gpu --format=csv,noheader |
    Set-Content (Join-Path $outputPath "gpu-after.txt")
python (Join-Path $PSScriptRoot "summarize.py") $outputPath
if ($LASTEXITCODE -ne 0) {
    throw "Research integrity/summary failed."
}
