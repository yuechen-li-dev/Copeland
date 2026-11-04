param(
    [string]$Output = 'artifacts/local/shader-refactor/final',
    [string]$Baseline = 'artifacts/local/rendering-gi/final',
    [string]$LightingFixture = 'artifacts/local/beacon-compiled-lighting'
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
function Resolve-RepositoryPath([string]$Value) {
    if ([IO.Path]::IsPathRooted($Value)) {
        return [IO.Path]::GetFullPath($Value)
    }
    return [IO.Path]::GetFullPath((Join-Path $repository $Value))
}
$baselinePath = Resolve-RepositoryPath $Baseline
if (-not (Test-Path -LiteralPath (Join-Path $baselinePath 'qualification.json'))) {
    throw "A preserved pre-refactor qualification is required: $baselinePath"
}

$sourcePaths = @(& git -C $repository ls-files -- '*.v.ts')
$sourcePaths += @(Get-ChildItem -LiteralPath (Join-Path $repository 'src/Copeland/Copeland.TS/Gpu') -Recurse -Filter '*.cs' |
    ForEach-Object { [IO.Path]::GetRelativePath($repository, $_.FullName) })
$sourcePaths += @(Get-ChildItem -LiteralPath (Join-Path $repository 'tests/Copeland/Copeland.TS.Tests') -Filter 'Gpu*Tests.cs' |
    ForEach-Object { [IO.Path]::GetRelativePath($repository, $_.FullName) })
$sourcePaths += @(& git -C $repository diff --name-only -- '*.cs')
$sourcePaths += @(
    'src/Copeland/Copeland.TS/Syntax/FlowExecutionProjection.cs',
    'src/Copeland/Copeland.TS/Syntax/FlowExecutionProjection.Stack.cs',
    'src/Copeland/Copeland.TS/Syntax/FlowExecutionProjection.Tape.cs',
    'tools/aurelian-shader-refactor/Qualify.ps1',
    'tools/aurelian-shader-refactor/compare_captures.py'
)
$sourcePaths = @($sourcePaths | ForEach-Object { $_.Replace('\', '/') } | Sort-Object -Unique)
& (Join-Path $PSScriptRoot '../aurelian-rendering-reference/Qualify.ps1') `
    -Output $Output -LightingFixture $LightingFixture -IncludeGi -AdditionalSources $sourcePaths

$outputPath = Resolve-RepositoryPath $Output
$qualificationPath = Join-Path $outputPath 'qualification.json'
$qualification = Get-Content -LiteralPath $qualificationPath -Raw | ConvertFrom-Json
$qualification.Accepted = $false
$qualification | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $qualificationPath
$python = Join-Path $repository 'artifacts/local/rendering-metrics-tools/venv/Scripts/python.exe'
& $python (Join-Path $PSScriptRoot 'compare_captures.py') $baselinePath $outputPath
if ($LASTEXITCODE -ne 0) { throw 'Refactor capture equivalence failed; qualification remains unaccepted.' }
$comparison = Get-Content -LiteralPath (Join-Path $outputPath 'capture-comparison.json') -Raw | ConvertFrom-Json
$qualification | Add-Member -NotePropertyName RefactorCaptureComparison -NotePropertyValue $comparison
$qualification.Accepted = $true
$qualification | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $qualificationPath
Write-Output "AURELIAN_SHADER_REFACTOR_QUALIFIED: $qualificationPath"
