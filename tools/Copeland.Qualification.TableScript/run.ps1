param(
    [ValidateSet("initializers", "row-authoring", "keyed-edits", "summaries", "snapshots")]
    [string[]]$Suite = @("initializers", "row-authoring", "keyed-edits", "summaries", "snapshots"),
    [string]$OutputDirectory = "artifacts/local/tablescript-qualification"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$localRoot = Join-Path $repositoryRoot "artifacts/local/"
if (-not $outputRoot.StartsWith($localRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Qualification output must be inside artifacts/local."
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$workbook = Join-Path $repositoryRoot "samples/copeland-ts/tablescript-friction-m12/Workbook/Workbook.ts"
$originalHash = (Get-FileHash -LiteralPath $workbook).Hash

Push-Location $repositoryRoot
try {
    & dotnet build src/Copeland/Copeland.Cli/Copeland.Cli.csproj -m:1 --nologo
    if ($LASTEXITCODE -ne 0) { throw "CLI build failed." }
    & dotnet build samples/copeland-ts/tablescript-friction-m12/TableScriptFrictionM12.slnx -m:1 --nologo
    if ($LASTEXITCODE -ne 0) { throw "Workbook build failed." }
    $consumer = & dotnet run --project samples/copeland-ts/tablescript-friction-m12/Consumer --no-build
    if ($LASTEXITCODE -ne 0) { throw "Workbook consumer assertions failed." }
    $consumer | Set-Content -LiteralPath (Join-Path $outputRoot "consumer.txt") -Encoding utf8

    $results = foreach ($name in $Suite) {
        $caseDirectory = Join-Path $outputRoot $name
        & (Join-Path $PSScriptRoot "cases/$name.ps1") -RepositoryRoot $repositoryRoot -OutputDirectory $caseDirectory | Out-Host
        if (-not $?) { throw "Suite '$name' failed." }
        $summary = Get-Content -LiteralPath (Join-Path $caseDirectory "summary.json") -Raw | ConvertFrom-Json
        if (-not $summary.success) { throw "Suite '$name' did not qualify." }
        [ordered]@{ suite = $name; checks = $summary.checks; success = $true }
    }
    [ordered]@{
        success = $true
        sourceSha256 = $originalHash
        suites = @($results)
        checks = ($results | ForEach-Object { $_.checks } | Measure-Object -Sum).Sum
        runtimeCoverage = "C# and CLI; JavaScript parity belongs to the compiler/browser suites."
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputRoot "summary.json") -Encoding utf8
}
finally {
    Pop-Location
    if ((Get-FileHash -LiteralPath $workbook).Hash -ne $originalHash) {
        throw "Qualification changed the authored workbook."
    }
}
