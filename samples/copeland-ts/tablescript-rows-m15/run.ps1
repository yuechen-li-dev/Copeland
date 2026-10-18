param([string]$BrowserChannel = 'msedge')

$ErrorActionPreference = 'Stop'
$fixtureRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $fixtureRoot '../../..')).Path

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE."
    }
}

Invoke-Checked 'npm.cmd' @('ci', '--prefix', $fixtureRoot, '--no-audit', '--no-fund')
$hostRuntime = Join-Path $fixtureRoot '../react-friction-m0/dist/browser/packages/copeland-browser-v1/index.js'
if (-not (Test-Path -LiteralPath $hostRuntime)) {
    # Materialize the existing canonical browser frame registry through its owner.
    $reactFixture = Join-Path $fixtureRoot '../react-friction-m0'
    $runtimeDirectory = Join-Path $reactFixture 'runtime/dist'
    New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $fixtureRoot '../copeland-website-m0/runtime/browser-v1.js') -Destination (Join-Path $runtimeDirectory 'browser-v1.js')
    Invoke-Checked 'dotnet' @('build', (Join-Path $repositoryRoot 'src/Copeland/Copeland.Cli/Copeland.Cli.csproj'), '-m:1', '--nologo')
    Invoke-Checked 'tspack' @('sync', '--root', $reactFixture)
    Invoke-Checked 'tspack' @('build', '--root', $reactFixture, 'browser')
}
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
