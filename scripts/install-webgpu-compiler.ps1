param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/local/webgpu-tools'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
cargo install naga-cli --version 27.0.0 --locked --root $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Naga CLI installation failed.' }
$name = if ($IsWindows) { 'naga.exe' } else { 'naga' }
$env:AURELIAN_NAGA = Join-Path $OutputDirectory "bin/$name"
& $env:AURELIAN_NAGA --version
if ($LASTEXITCODE -ne 0) { throw 'Installed Naga could not run.' }
Write-Host "AURELIAN_NAGA=$env:AURELIAN_NAGA"
