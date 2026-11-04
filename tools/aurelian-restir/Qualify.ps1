param(
    [string]$Output = 'artifacts/local/rendering-restir/final',
    [string]$LightingFixture = 'artifacts/local/beacon-compiled-lighting'
)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot '../aurelian-rendering-reference/Qualify.ps1') `
    -Output $Output -LightingFixture $LightingFixture -IncludeRestir
