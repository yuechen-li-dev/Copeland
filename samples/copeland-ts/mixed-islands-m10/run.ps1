param(
    [switch]$FullValidation,
    [string]$BrowserChannel = 'msedge',
    [string]$EvidenceName = 'mixed-islands-m10'
)

$ErrorActionPreference = 'Stop'
$fixtureRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $fixtureRoot '../../..')).Path
$sharedFixture = Join-Path $fixtureRoot '../presentation-friction-m9'
$reactFixture = Join-Path $fixtureRoot '../react-friction-m0'
$evidenceRoot = Join-Path $repositoryRoot "docs/Copeland/reviews/evidence/$EvidenceName"
$logRoot = Join-Path $repositoryRoot 'artifacts/validation'
New-Item -ItemType Directory -Path $evidenceRoot, $logRoot -Force | Out-Null

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments, [string]$LogPath)
    if ($LogPath) {
        & $Executable @Arguments 2>&1 | Tee-Object -FilePath $LogPath
    } else {
        & $Executable @Arguments
    }
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE"
    }
}

Invoke-Checked 'dotnet' @('build', (Join-Path $repositoryRoot 'src/Copeland/Copeland.Cli/Copeland.Cli.csproj'), '-m:1', '-v:minimal')
Invoke-Checked 'npm.cmd' @('ci', '--prefix', $fixtureRoot, '--no-audit', '--no-fund')
$runtimeRoot = Join-Path $reactFixture 'runtime/dist'
New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $fixtureRoot '../copeland-website-m0/runtime/browser-v1.js') -Destination (Join-Path $runtimeRoot 'browser-v1.js')
Invoke-Checked 'tspack' @('sync', '--root', $reactFixture)
Invoke-Checked 'tspack' @('build', '--root', $reactFixture, 'browser')
Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $fixtureRoot 'tooling/compiler'), '--', $fixtureRoot, $evidenceRoot)
Invoke-Checked 'npm.cmd' @('run', 'check', '--prefix', $fixtureRoot)
Invoke-Checked 'npm.cmd' @('run', 'build', '--prefix', $fixtureRoot)
$previousBrowser = $env:FRICTION_BROWSER_CHANNEL
$previousEvidence = $env:FRICTION_PRESENTATION_EVIDENCE_NAME
$previousMixedEvidence = $env:FRICTION_MIXED_EVIDENCE_NAME
try {
    $env:FRICTION_BROWSER_CHANNEL = $BrowserChannel
    $env:FRICTION_MIXED_EVIDENCE_NAME = $EvidenceName
    Invoke-Checked 'npm.cmd' @('run', 'qualify', '--prefix', $fixtureRoot)
    if ($FullValidation) {
        if (-not (Test-Path (Join-Path $sharedFixture 'node_modules/playwright'))) {
            Invoke-Checked 'npm.cmd' @('ci', '--prefix', $sharedFixture, '--no-audit', '--no-fund')
        }
        $sharedEvidence = Join-Path $evidenceRoot 'shared-source-regression'
        Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $sharedFixture 'tooling/compiler'), '--', $sharedFixture, $sharedEvidence)
        Invoke-Checked 'npm.cmd' @('run', 'check', '--prefix', $sharedFixture)
        Invoke-Checked 'npm.cmd' @('run', 'build', '--prefix', $sharedFixture)
        $env:FRICTION_PRESENTATION_EVIDENCE_NAME = "$EvidenceName/shared-source-regression"
        Invoke-Checked 'npm.cmd' @('run', 'qualify', '--prefix', $sharedFixture)
        & (Join-Path $sharedFixture 'tooling/react-regression.ps1') -BrowserChannel $BrowserChannel -EvidenceName "$EvidenceName/react-regression"
    }
} finally {
    $env:FRICTION_BROWSER_CHANNEL = $previousBrowser
    $env:FRICTION_PRESENTATION_EVIDENCE_NAME = $previousEvidence
    $env:FRICTION_MIXED_EVIDENCE_NAME = $previousMixedEvidence
}

if ($FullValidation) {
    Invoke-Checked 'dotnet' @('build', (Join-Path $repositoryRoot 'Copeland.slnx'), '-m:1', '-v:minimal') -LogPath (Join-Path $logRoot "$EvidenceName-build.log")
    Invoke-Checked 'dotnet' @('test', (Join-Path $repositoryRoot 'Copeland.slnx'), '--no-build', '-m:1') -LogPath (Join-Path $logRoot "$EvidenceName-tests.log")
    try {
        $env:FRICTION_MIXED_EVIDENCE_NAME = $EvidenceName
        Invoke-Checked 'node' @((Join-Path $fixtureRoot 'tooling/validation.ts'))
    } finally {
        $env:FRICTION_MIXED_EVIDENCE_NAME = $previousMixedEvidence
    }
}
