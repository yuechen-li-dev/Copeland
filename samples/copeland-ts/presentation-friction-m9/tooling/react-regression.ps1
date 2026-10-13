param([string]$BrowserChannel = 'msedge')

$ErrorActionPreference = 'Stop'
$fixtureRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repositoryRoot = (Resolve-Path (Join-Path $fixtureRoot '../../..')).Path
$legacyRoot = Join-Path $fixtureRoot '../react-friction-m0'
$evidenceName = 'presentation-friction-m9/react-regression'
$evidenceRoot = Join-Path $repositoryRoot "docs/Copeland/reviews/evidence/$evidenceName"
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path (Join-Path $legacyRoot 'baseline/node_modules/playwright'))) {
    Invoke-Checked 'npm.cmd' @('ci', '--prefix', (Join-Path $legacyRoot 'baseline'), '--no-audit', '--no-fund')
}
Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $legacyRoot 'tooling/compiler-probes/CompilerProbes.csproj'), '--',
    (Join-Path $legacyRoot 'lifecycle-corpus.json'), (Join-Path $evidenceRoot 'compiler-report.json'))

$previousEvidence = $env:FRICTION_EVIDENCE_NAME
$previousBrowser = $env:FRICTION_BROWSER_CHANNEL
$previousStabilization = $env:FRICTION_STABILIZATION
$previousProfile = $env:FRICTION_COMPONENT_PROFILE
try {
    $env:FRICTION_EVIDENCE_NAME = $evidenceName
    $env:FRICTION_BROWSER_CHANNEL = $BrowserChannel
    $env:FRICTION_STABILIZATION = '1'
    foreach ($profile in @('Diagnostic', 'Symbolic', 'Production')) {
        $env:FRICTION_COMPONENT_PROFILE = $profile
        Invoke-Checked 'node' @((Join-Path $legacyRoot 'tooling/state-on.ts'))
        Invoke-Checked 'node' @((Join-Path $legacyRoot 'tooling/lifecycle.ts'))
    }
    Invoke-Checked 'node' @((Join-Path $legacyRoot 'tooling/qualify.ts'))
    Invoke-Checked 'node' @((Join-Path $legacyRoot 'tooling/semantic-ui.ts'))
} finally {
    $env:FRICTION_EVIDENCE_NAME = $previousEvidence
    $env:FRICTION_BROWSER_CHANNEL = $previousBrowser
    $env:FRICTION_STABILIZATION = $previousStabilization
    $env:FRICTION_COMPONENT_PROFILE = $previousProfile
}
