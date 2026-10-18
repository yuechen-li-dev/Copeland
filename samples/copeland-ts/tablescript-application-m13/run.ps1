param([string]$BrowserChannel = 'msedge')

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot '../../../tools/Copeland.Qualification.Browser/run.ps1') `
    -SampleRoot $PSScriptRoot -BrowserChannel $BrowserChannel
