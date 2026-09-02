param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

$artifactRoot = Join-Path $RepositoryRoot "artifacts/cts-opt-m1"
$temporaryRoot = Join-Path $RepositoryRoot ".tmp/cts-opt-m1-benchmark"
New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null

$beforePath = Join-Path $artifactRoot "benchmark-before.js"
$afterPath = Join-Path $artifactRoot "benchmark-after.js"

function Get-Median([double[]]$Values) {
    $ordered = $Values | Sort-Object
    $middle = [Math]::Floor($ordered.Count / 2)
    if (($ordered.Count % 2) -eq 1) {
        return $ordered[$middle]
    }
    return ($ordered[$middle - 1] + $ordered[$middle]) / 2
}

function Measure-Startup([string]$Path) {
    $samples = [System.Collections.Generic.List[double]]::new()
    for ($index = 0; $index -lt 15; $index += 1) {
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        & node $Path | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Node startup measurement failed for $Path."
        }
        $watch.Stop()
        $samples.Add($watch.Elapsed.TotalMilliseconds)
    }
    return [ordered]@{
        medianMs = [Math]::Round((Get-Median $samples.ToArray()), 3)
        minimumMs = [Math]::Round(($samples | Measure-Object -Minimum).Minimum, 3)
        samplesMs = @($samples | ForEach-Object { [Math]::Round($_, 3) })
    }
}

function Write-BenchmarkScript([string]$SourcePath, [string]$DestinationPath) {
    $harness = @'

const __cope_benchmarks = { rowAccess, columnAccess, cellAccess, queryAccess };
const __cope_results = {};
for (const [name, benchmark] of Object.entries(__cope_benchmarks)) {
    for (let warmup = 0; warmup < 50; warmup += 1) benchmark(1000);
    const iterations = name === "queryAccess" ? 20000 : 100000;
    const started = process.hrtime.bigint();
    const checksum = benchmark(iterations);
    const elapsed = process.hrtime.bigint() - started;
    __cope_results[name] = { nanosecondsPerIteration: Number(elapsed) / iterations, checksum };
}
console.log(JSON.stringify(__cope_results));
'@
    $source = [System.IO.File]::ReadAllText($SourcePath)
    [System.IO.File]::WriteAllText($DestinationPath, $source + $harness, [System.Text.UTF8Encoding]::new($false))
}

function Measure-SteadyState([string]$SourcePath, [string]$Label) {
    $scriptPath = Join-Path $temporaryRoot "$Label.js"
    Write-BenchmarkScript -SourcePath $SourcePath -DestinationPath $scriptPath
    $samples = [ordered]@{
        rowAccess = [System.Collections.Generic.List[double]]::new()
        columnAccess = [System.Collections.Generic.List[double]]::new()
        cellAccess = [System.Collections.Generic.List[double]]::new()
        queryAccess = [System.Collections.Generic.List[double]]::new()
    }
    for ($run = 0; $run -lt 7; $run += 1) {
        $output = & node $scriptPath
        if ($LASTEXITCODE -ne 0) {
            throw "Node steady-state measurement failed for $Label."
        }
        $parsed = $output | ConvertFrom-Json
        foreach ($name in $samples.Keys) {
            $samples[$name].Add([double]$parsed.$name.nanosecondsPerIteration)
        }
    }
    $result = [ordered]@{}
    foreach ($name in $samples.Keys) {
        $result[$name] = [ordered]@{
            medianNanosecondsPerIteration = [Math]::Round((Get-Median $samples[$name].ToArray()), 3)
            samplesNanosecondsPerIteration = @($samples[$name] | ForEach-Object { [Math]::Round($_, 3) })
        }
    }
    return $result
}

$beforeStartup = Measure-Startup $beforePath
$afterStartup = Measure-Startup $afterPath
$beforeSteady = Measure-SteadyState -SourcePath $beforePath -Label "before"
$afterSteady = Measure-SteadyState -SourcePath $afterPath -Label "after"

$result = [ordered]@{
    milestone = "CTS-OPT-M1"
    nodeVersion = (& node --version)
    startup = [ordered]@{
        before = $beforeStartup
        after = $afterStartup
        medianChangeMs = [Math]::Round($afterStartup.medianMs - $beforeStartup.medianMs, 3)
    }
    steadyState = [ordered]@{
        before = $beforeSteady
        after = $afterSteady
    }
    notes = @(
        "Startup is 15 fresh Node processes per artifact measured from PowerShell.",
        "Steady-state medians use seven fresh Node processes after in-process warmup.",
        "Row, column, and cell units are one checked access; query is one ten-cell sum."
    )
}
$json = $result | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText(
    (Join-Path $artifactRoot "runtime-comparison.json"),
    $json + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))
Write-Output $json
