param(
    [string]$Output = "artifacts/local/vulkan-profile/native-latency",
    [string]$VulkanSdk = "C:/VulkanSDK/1.4.350.0"
)

$ErrorActionPreference = "Stop"
$repository = (Resolve-Path "$PSScriptRoot/../..").Path
Set-Location $repository
if (Test-Path -LiteralPath $Output) {
    throw "Native control output must be fresh: $Output"
}
New-Item -ItemType Directory $Output | Out-Null
$outputPath = (Resolve-Path $Output).Path
$source = "$PSScriptRoot/native_vulkan_latency.cpp"
$executable = "$outputPath/native_vulkan_latency.exe"
& clang++ -std=c++20 -O2 -I "$VulkanSdk/Include" $source "$VulkanSdk/Lib/vulkan-1.lib" -o $executable *> "$outputPath/build.log"
if ($LASTEXITCODE -ne 0) {
    throw "Native latency control compilation failed. See $outputPath/build.log"
}
$protocol = @{
    Accepted = $false
    SourceSha256 = (Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant()
    BinarySha256 = (Get-FileHash $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    Compiler = (& clang++ --version | Out-String)
    ScriptSha256 = (Get-FileHash $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
    VulkanSdk = $VulkanSdk
    Runs = @()
}
$protocolPath = "$outputPath/protocol.json"
$protocol | ConvertTo-Json -Depth 8 | Set-Content $protocolPath
for ($run = 1; $run -le 3; $run++) {
    Start-Sleep -Seconds 20
    $clocks = (& nvidia-smi --query-gpu=name,pci.bus_id,pstate,clocks.sm,clocks.mem --format=csv | Out-String)
    & $executable > "$outputPath/run$run.csv"
    if ($LASTEXITCODE -ne 0) {
        throw "Native latency control failed in run $run"
    }
    $lines = Get-Content "$outputPath/run$run.csv"
    $rows = $lines | Select-Object -Skip 1 | ConvertFrom-Csv
    if ($rows.Count -ne 1500) {
        throw "Incomplete native latency sample count: $($rows.Count)"
    }
    foreach ($mode in @("timeline", "fence", "idle")) {
        $selected = @($rows | Where-Object Mode -eq $mode)
        if ($selected.Count -ne 500 -or @($selected.Sample | Select-Object -Unique).Count -ne 500) {
            throw "Incomplete or duplicate native latency grid for $mode"
        }
        foreach ($row in $selected) {
            if ([double]$row.WallMicroseconds -le 0 -or [double]$row.CompletionWaitMicroseconds -le 0) {
                throw "Invalid native latency timing"
            }
        }
    }
    $protocol.Runs += @{
        Run = $run
        Device = $lines[0]
        ClocksBefore = $clocks
        CsvSha256 = (Get-FileHash "$outputPath/run$run.csv" -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $protocol | ConvertTo-Json -Depth 8 | Set-Content $protocolPath
}
$protocol.Accepted = $true
$protocol | ConvertTo-Json -Depth 8 | Set-Content $protocolPath
Write-Host "AURELIAN_NATIVE_LATENCY_CONTROL_PASSED"
