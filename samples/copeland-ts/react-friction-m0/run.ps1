param(
    [switch]$RefreshLock,
    [switch]$FullValidation,
    [switch]$Stabilization = $true,
    [switch]$Grammar = $true,
    [switch]$LayoutTemplates = $true,
    [string]$BrowserChannel = 'msedge'
)

$ErrorActionPreference = 'Stop'
$fixtureRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $fixtureRoot '../../..')).Path
$evidenceName = if ($LayoutTemplates) { 'react-ui-layout-templates-m4' } elseif ($Grammar) { 'react-ui-grammar-m3' } elseif ($Stabilization) { 'react-ui-stabilization-m1' } else { 'react-friction-m0' }
$corpusName = if ($LayoutTemplates) { 'layout-templates-corpus.json' } elseif ($Grammar) { 'grammar-corpus.json' } elseif ($Stabilization) { 'stabilization-corpus.json' } else { 'friction-corpus.json' }
$evidenceRoot = Join-Path $repositoryRoot "docs/Copeland/reviews/evidence/$evidenceName"
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE."
    }
}

Invoke-Checked 'dotnet' @('build', (Join-Path $repositoryRoot 'src/Copeland/Copeland.Cli/Copeland.Cli.csproj'), '-m:1', '-v:minimal')
if (-not (Test-Path (Join-Path $fixtureRoot 'baseline/node_modules/typescript/bin/tsc'))) {
    Invoke-Checked 'npm.cmd' @('ci', '--prefix', (Join-Path $fixtureRoot 'baseline'), '--no-audit', '--no-fund')
}
Invoke-Checked 'npm.cmd' @('run', 'check', '--prefix', (Join-Path $fixtureRoot 'baseline'))
Invoke-Checked 'node' @((Join-Path $fixtureRoot 'baseline/node_modules/typescript/bin/tsc'), '--project', (Join-Path $fixtureRoot 'tooling/tsconfig.json'))
Invoke-Checked 'npm.cmd' @('run', 'build', '--prefix', (Join-Path $fixtureRoot 'baseline'))
if ($RefreshLock) {
    Invoke-Checked 'tspack' @('update', '--root', $fixtureRoot)
}
Invoke-Checked 'tspack' @('sync', '--root', $fixtureRoot)
Invoke-Checked 'tspack' @('build', '--root', $fixtureRoot, 'browser')
Invoke-Checked 'tspack' @('sync', '--root', (Join-Path $fixtureRoot '../copeland-website-m0'))
Invoke-Checked 'tspack' @('build', '--root', (Join-Path $fixtureRoot '../copeland-website-m0'), 'browser')
Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $fixtureRoot 'tooling/compiler-probes/CompilerProbes.csproj'), '--', (Join-Path $fixtureRoot $corpusName), (Join-Path $evidenceRoot 'compiler-report.json'))

$previousBrowserChannel = $env:FRICTION_BROWSER_CHANNEL
$previousStabilization = $env:FRICTION_STABILIZATION
$previousEvidenceName = $env:FRICTION_EVIDENCE_NAME
try {
    $env:FRICTION_BROWSER_CHANNEL = $BrowserChannel
    $env:FRICTION_STABILIZATION = if ($Stabilization) { '1' } else { '0' }
    $env:FRICTION_EVIDENCE_NAME = $evidenceName
    Invoke-Checked 'node' @((Join-Path $fixtureRoot 'tooling/qualify.ts'))
    Invoke-Checked 'node' @((Join-Path $fixtureRoot 'tooling/website-regression.ts'))
    Invoke-Checked 'node' @((Join-Path $fixtureRoot 'tooling/analyze.ts'))
} finally {
    $env:FRICTION_BROWSER_CHANNEL = $previousBrowserChannel
    $env:FRICTION_STABILIZATION = $previousStabilization
    $env:FRICTION_EVIDENCE_NAME = $previousEvidenceName
}

if ($FullValidation) {
    Invoke-Checked 'dotnet' @('build', (Join-Path $repositoryRoot 'Copeland.slnx'), '-m:1', '-v:minimal')
    Invoke-Checked 'dotnet' @('test', (Join-Path $repositoryRoot 'Copeland.slnx'), '--no-build', '-m:1')
}
