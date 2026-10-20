param(
    [string]$Output = "artifacts/local/scheduling-clock/qualified",
    [string]$Fixtures = "artifacts/local/kernel-comparison/fixtures",
    [string]$Shaders = "artifacts/local/scheduling-research/qualified/run1/vulkan/shaders",
    [string]$Python = "artifacts/local/kernel-comparison/venv/Scripts/python.exe",
    [ValidateRange(1, 5)][int]$Rounds = 3
)

$ErrorActionPreference = "Stop"
Set-Location (Resolve-Path "$PSScriptRoot/../..").Path
if (Test-Path -LiteralPath $Output) {
    throw "Clock research output must be fresh: $Output"
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

Invoke-Checked "dotnet" @("build", "tools/Aurelian.ClockResearch", "-c", "Release", "-m:1",
    "-nr:false", "-p:UseSharedCompilation=false") "$outputPath/build.log"
$protocol = @{
    Accepted = $false
    Rounds = $Rounds
    Commit = (& git rev-parse HEAD).Trim()
    Intervention = "Vulkan prior workload only; identical measured command buffers; no clock settings changed; scoped telemetry process."
    Sources = @{}
}
foreach ($source in @($PSCommandPath, "tools/Aurelian.ClockResearch/Program.cs", "$PSScriptRoot/summarize_clocks.py")) {
    $protocol.Sources[(Resolve-Path $source).Path] = (Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant()
}
$protocolPath = "$outputPath/protocol.json"
$protocol | ConvertTo-Json -Depth 8 | Set-Content $protocolPath
$dll = "tools/Aurelian.ClockResearch/bin/Release/net10.0/Aurelian.ClockResearch.dll"
Invoke-Checked "dotnet" @($dll, $Fixtures, $Shaders, "$outputPath/validation", "--validation") "$outputPath/validation.log"
if (Select-String -Path "$outputPath/validation.log" -Pattern "Validation Error|Validation Warning" -Quiet) {
    throw "Clock control emitted Vulkan validation diagnostics."
}
for ($round = 1; $round -le $Rounds; $round++) {
    Write-Host "Vulkan-only clock control $round/$Rounds"
    Invoke-Checked "dotnet" @($dll, $Fixtures, $Shaders, "$outputPath/run$round") "$outputPath/run$round.log"
    Invoke-Checked $Python @("tools/kernel-comparison/summarize_clocks.py", "$outputPath/run$round") "$outputPath/run$round-summary.txt"
}
foreach ($source in $protocol.Sources.Keys) {
    if ((Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $protocol.Sources[$source]) {
        throw "Clock research source changed: $source"
    }
}
$protocol.Accepted = $true
$protocol | ConvertTo-Json -Depth 8 | Set-Content $protocolPath
