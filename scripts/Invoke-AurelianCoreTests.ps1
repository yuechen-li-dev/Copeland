param(
    [string]$ResultsDirectory = 'artifacts/local/core-ci',
    [switch]$BrowserToolchain
)

$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
[xml]$solution = Get-Content -LiteralPath (Join-Path $repository 'Aurelian.Core.slnx') -Raw
$failures = [System.Collections.Generic.List[string]]::new()
$selectedAssemblies = 0
$filter = 'Category!=WebGpuToolchain'
if ($BrowserToolchain) { $filter = 'Category=WebGpuToolchain' }
$resultsRoot = [System.IO.Path]::GetFullPath((Join-Path $repository $ResultsDirectory))

foreach ($project in $solution.SelectNodes('//Project')) {
    if (-not $project.Path.StartsWith('tests/')) { continue }
    $name = [System.IO.Path]::GetFileNameWithoutExtension($project.Path)
    if ($BrowserToolchain -and $name -ne 'Aurelian.Shaders.Tests') { continue }
    $selectedAssemblies++
    # A distinct invocation directory prevents stale or overwritten TRX files from passing a run.
    $results = Join-Path (Join-Path $resultsRoot $name) ([guid]::NewGuid().ToString('N'))
    & dotnet test (Join-Path $repository $project.Path) -c Release --no-build --filter $filter --logger trx --results-directory $results
    $exitCode = $LASTEXITCODE
    $reports = @(Get-ChildItem -LiteralPath $results -Filter '*.trx' -ErrorAction SilentlyContinue)
    if ($exitCode -ne 0 -or $reports.Count -ne 1) {
        $failures.Add("${name}: exit=$exitCode, reports=$($reports.Count)")
        continue
    }
    [xml]$report = Get-Content -LiteralPath $reports[0].FullName -Raw
    $counters = $report.TestRun.ResultSummary.Counters
    if ([int]$counters.executed -le 0 -or [int]$counters.passed -ne [int]$counters.executed -or
        [int]$counters.failed -ne 0 -or [int]$counters.error -ne 0 -or
        $report.TestRun.ResultSummary.Outcome -notin @('Completed', 'Passed')) {
        $failures.Add("${name}: incomplete or failed test execution")
    }
}
if ($selectedAssemblies -eq 0) { throw 'No test assemblies selected from the core solution.' }
if ($failures.Count -gt 0) {
    throw ("Core test validation failed:`n" + ($failures -join "`n"))
}
Write-Output 'Aurelian core tests passed with nonempty execution evidence for every selected assembly.'
