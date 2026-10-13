param(
    [switch]$FullValidation,
    [switch]$ReactRegression,
    [string]$BrowserChannel = 'msedge'
)

$ErrorActionPreference = 'Stop'
$fixtureRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $fixtureRoot '../../..')).Path
$reactFixture = Join-Path $fixtureRoot '../react-friction-m0'
$evidenceRoot = Join-Path $repositoryRoot 'docs/Copeland/reviews/evidence/presentation-friction-m9'
$logRoot = Join-Path $repositoryRoot 'artifacts/validation'
New-Item -ItemType Directory -Path $evidenceRoot, $logRoot -Force | Out-Null

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments, [string]$LogPath)
    if ($LogPath) {
        & $Executable @Arguments 2>&1 | Tee-Object -FilePath $LogPath
    } else {
        & $Executable @Arguments
    }
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed with exit code $LASTEXITCODE" }
}

Invoke-Checked 'dotnet' @('build', (Join-Path $repositoryRoot 'src/Copeland/Copeland.Cli/Copeland.Cli.csproj'), '-m:1', '-v:minimal')
Invoke-Checked 'npm.cmd' @('ci', '--prefix', $fixtureRoot, '--no-audit', '--no-fund')
# TSPack owns the canonical browser registry materialization. Reuse its existing
# locked host fixture; this demo never copies or rewrites the registry engine.
$runtimeRoot = Join-Path $reactFixture 'runtime/dist'
New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $fixtureRoot '../copeland-website-m0/runtime/browser-v1.js') -Destination (Join-Path $runtimeRoot 'browser-v1.js')
Invoke-Checked 'tspack' @('sync', '--root', $reactFixture)
Invoke-Checked 'tspack' @('build', '--root', $reactFixture, 'browser')
Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $fixtureRoot 'tooling/compiler'), '--', $fixtureRoot)
Invoke-Checked 'npm.cmd' @('run', 'check', '--prefix', $fixtureRoot)
Invoke-Checked 'npm.cmd' @('run', 'build', '--prefix', $fixtureRoot)
$previousChannel = $env:FRICTION_BROWSER_CHANNEL
try {
    $env:FRICTION_BROWSER_CHANNEL = $BrowserChannel
    Invoke-Checked 'npm.cmd' @('run', 'qualify', '--prefix', $fixtureRoot)
} finally {
    $env:FRICTION_BROWSER_CHANNEL = $previousChannel
}

if ($FullValidation -or $ReactRegression) {
    & (Join-Path $fixtureRoot 'tooling/react-regression.ps1') -BrowserChannel $BrowserChannel
}
if ($FullValidation) {
    Invoke-Checked 'dotnet' @('build', (Join-Path $repositoryRoot 'Copeland.slnx'), '-m:1', '-v:minimal') -LogPath (Join-Path $logRoot 'presentation-friction-m9-build.log')
    Invoke-Checked 'dotnet' @('test', (Join-Path $repositoryRoot 'Copeland.slnx'), '--no-build', '-m:1') -LogPath (Join-Path $logRoot 'presentation-friction-m9-tests.log')
    Invoke-Checked 'node' @((Join-Path $fixtureRoot 'tooling/validation.ts'))
}
Invoke-Checked 'node' @((Join-Path $fixtureRoot 'tooling/accounting.ts'))
