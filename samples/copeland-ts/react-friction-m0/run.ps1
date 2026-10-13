param(
    [switch]$RefreshLock,
    [switch]$FullValidation,
    [switch]$Stabilization = $true,
    [switch]$Grammar = $true,
    [switch]$LayoutTemplates = $true,
    [switch]$SemanticApi = $true,
    [switch]$StateOn = $true,
    [switch]$EffectsChildren = $true,
    [string]$BrowserChannel = 'msedge'
)

$ErrorActionPreference = 'Stop'
$fixtureRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $fixtureRoot '../../..')).Path
$evidenceName = if ($EffectsChildren) { 'react-effects-children-m7' } elseif ($StateOn) { 'react-state-on-m6' } elseif ($SemanticApi) { 'react-ui-semantic-api-m5' } elseif ($LayoutTemplates) { 'react-ui-layout-templates-m4' } elseif ($Grammar) { 'react-ui-grammar-m3' } elseif ($Stabilization) { 'react-ui-stabilization-m1' } else { 'react-friction-m0' }
$corpusName = if ($EffectsChildren) { 'lifecycle-corpus.json' } elseif ($StateOn) { 'state-on-corpus.json' } elseif ($SemanticApi) { 'semantic-ui-corpus.json' } elseif ($LayoutTemplates) { 'layout-templates-corpus.json' } elseif ($Grammar) { 'grammar-corpus.json' } elseif ($Stabilization) { 'stabilization-corpus.json' } else { 'friction-corpus.json' }
$evidenceRoot = Join-Path $repositoryRoot "docs/Copeland/reviews/evidence/$evidenceName"
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments, [string]$LogPath)
    if ($LogPath) {
        & $Executable @Arguments 2>&1 | Tee-Object -FilePath $LogPath
    } else {
        & $Executable @Arguments
    }
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
$previousComponentProfile = $env:FRICTION_COMPONENT_PROFILE
try {
    $env:FRICTION_BROWSER_CHANNEL = $BrowserChannel
    $env:FRICTION_STABILIZATION = if ($Stabilization) { '1' } else { '0' }
    $env:FRICTION_EVIDENCE_NAME = $evidenceName
    Invoke-Checked 'node' @((Join-Path $fixtureRoot 'tooling/qualify.ts'))
    if ($SemanticApi) {
        Invoke-Checked 'node' @((Join-Path $fixtureRoot 'tooling/semantic-ui.ts'))
    }
    if ($StateOn) {
        foreach ($componentProfile in @('Diagnostic', 'Symbolic', 'Production')) {
            $env:FRICTION_COMPONENT_PROFILE = $componentProfile
            Invoke-Checked 'node' @((Join-Path $fixtureRoot 'tooling/state-on.ts'))
            if ($EffectsChildren) {
                Invoke-Checked 'node' @((Join-Path $fixtureRoot 'tooling/lifecycle.ts'))
            }
        }
    }
    Invoke-Checked 'node' @((Join-Path $fixtureRoot 'tooling/website-regression.ts'))
    Invoke-Checked 'node' @((Join-Path $fixtureRoot 'tooling/analyze.ts'))
} finally {
    $env:FRICTION_BROWSER_CHANNEL = $previousBrowserChannel
    $env:FRICTION_STABILIZATION = $previousStabilization
    $env:FRICTION_EVIDENCE_NAME = $previousEvidenceName
    $env:FRICTION_COMPONENT_PROFILE = $previousComponentProfile
}

if ($FullValidation) {
    $validationLogRoot = Join-Path $repositoryRoot 'artifacts/validation'
    New-Item -ItemType Directory -Path $validationLogRoot -Force | Out-Null
    $buildLog = Join-Path $validationLogRoot "$evidenceName-build.log"
    $testLog = Join-Path $validationLogRoot "$evidenceName-tests.log"
    Invoke-Checked 'dotnet' @('build', (Join-Path $repositoryRoot 'Copeland.slnx'), '-m:1', '-v:minimal') -LogPath $buildLog
    Invoke-Checked 'dotnet' @('test', (Join-Path $repositoryRoot 'Copeland.slnx'), '--no-build', '-m:1') -LogPath $testLog
    if ($EffectsChildren) {
        Invoke-Checked 'node' @((Join-Path $fixtureRoot 'tooling/record-lifecycle-validation.ts'))
    }
}
