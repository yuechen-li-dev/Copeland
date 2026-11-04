param(
    [string]$Output = 'artifacts/local/rendering-sky/final',
    [string]$LightingFixture = 'artifacts/local/beacon-compiled-lighting'
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$sourcePaths = @(Get-ChildItem -LiteralPath (Join-Path $repository 'src/Copeland/Copeland.TS/Gpu') -Recurse -Filter '*.cs' |
    ForEach-Object { [IO.Path]::GetRelativePath($repository, $_.FullName) })
$sourcePaths += @(Get-ChildItem -LiteralPath (Join-Path $repository 'src/Aurelian/Aurelian.Shaders/Assets') -Recurse -Filter '*.v.ts' |
    ForEach-Object { [IO.Path]::GetRelativePath($repository, $_.FullName) })
& (Join-Path $PSScriptRoot '../aurelian-rendering-reference/Qualify.ps1') `
    -Output $Output -LightingFixture $LightingFixture -IncludeSky -AdditionalSources $sourcePaths
