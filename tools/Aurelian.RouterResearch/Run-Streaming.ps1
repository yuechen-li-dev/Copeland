param(
    [string]$Output = "artifacts/local/router-research/streaming-qualified"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
Set-Location -LiteralPath $repoRoot
$outputPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $Output))
if (Test-Path -LiteralPath $outputPath) {
    throw "Streaming qualification needs a fresh output directory."
}
New-Item -ItemType Directory -Path $outputPath | Out-Null
$previousTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = "0"
    dotnet build tools/Aurelian.RouterResearch -c Release -m:1 -nr:false -p:UseSharedCompilation=false *> (Join-Path $outputPath "build.log")
    if ($LASTEXITCODE -ne 0) {
        throw "Streaming tool build failed."
    }
    foreach ($name in @("validation", "round1", "round2", "round3")) {
        $runArguments = @("run", "--project", "tools/Aurelian.RouterResearch", "-c", "Release", "--no-build", "--", "--streaming", (Join-Path $outputPath $name))
        if ($name -eq "validation") {
            $runArguments += "--validation"
        }
        $logPath = Join-Path $outputPath "$name.log"
        & dotnet @runArguments *> $logPath
        if ($LASTEXITCODE -ne 0) {
            throw "Streaming $name failed; see $logPath."
        }
        if (Select-String -LiteralPath $logPath -Pattern "Validation Error|VUID-" -Quiet) {
            throw "Streaming $name reported Vulkan diagnostics."
        }
        Get-Content -LiteralPath $logPath -Tail 1
    }
}
finally {
    $env:DOTNET_TieredCompilation = $previousTiering
}
