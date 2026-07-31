[CmdletBinding()]
param(
    [string]$ReleaseDirectory = "",
    [switch]$OpenCode
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $ReleaseDirectory) {
    $ReleaseDirectory = Join-Path $repositoryRoot "artifacts/human-workflow-m0/release-candidate"
}

$releaseRoot = (Resolve-Path $ReleaseDirectory).Path
$sampleRoot = Join-Path $repositoryRoot "samples/copeland-ts/CopelandHello"
$proofRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("Copeland-Human-Workflow-" + [Guid]::NewGuid().ToString("N"))
$projectRoot = Join-Path $proofRoot "CopelandHello"
$userDataRoot = Join-Path $proofRoot "vscode-user-data"
$extensionsRoot = Join-Path $proofRoot "vscode-extensions"
$nugetRoot = Join-Path $proofRoot "nuget-packages"
$vsix = Join-Path $releaseRoot "copeland-ts-0.1.0-preview.1.vsix"

function Assert-LastExitCode {
    param([string]$Operation)

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Path $projectRoot, $userDataRoot, $extensionsRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $sampleRoot "CopelandHello.slnx") -Destination $proofRoot
Copy-Item -LiteralPath (Join-Path $sampleRoot "CopelandHello/CopelandHello.csproj") -Destination $projectRoot
Copy-Item -LiteralPath (Join-Path $sampleRoot "CopelandHello/package.json") -Destination $projectRoot
Copy-Item -LiteralPath (Join-Path $sampleRoot "CopelandHello/package-lock.json") -Destination $projectRoot
Copy-Item -LiteralPath (Join-Path $sampleRoot "CopelandHello/tsconfig.tsx") -Destination $projectRoot
Copy-Item -LiteralPath (Join-Path $sampleRoot "CopelandHello/Program.cs") -Destination $projectRoot
Copy-Item -Recurse -LiteralPath (Join-Path $sampleRoot "CopelandHello/src") -Destination $projectRoot
Copy-Item -Recurse -LiteralPath (Join-Path $sampleRoot "CopelandHello/contracts") -Destination $projectRoot
Copy-Item -Recurse -LiteralPath (Join-Path $sampleRoot "CopelandHello/scripts") -Destination $projectRoot

$env:NUGET_PACKAGES = $nugetRoot
Push-Location $projectRoot
try {
    dotnet new tool-manifest
    Assert-LastExitCode "dotnet new tool-manifest"
    dotnet tool install Copeland.TS.Tool `
        --version 0.1.0-preview.1 `
        --add-source $releaseRoot
    Assert-LastExitCode "dotnet tool install"
    npm ci
    Assert-LastExitCode "npm ci"
    dotnet restore --source $releaseRoot
    Assert-LastExitCode "dotnet restore"
    dotnet build --no-restore
    Assert-LastExitCode "dotnet build"
    dotnet run --no-build --no-restore
    Assert-LastExitCode "dotnet run"
}
finally {
    Pop-Location
}

$code = Join-Path $env:LOCALAPPDATA "Programs/Microsoft VS Code/bin/code.cmd"
& $code `
    --user-data-dir $userDataRoot `
    --extensions-dir $extensionsRoot `
    --install-extension $vsix `
    --force
Assert-LastExitCode "VSIX installation"
& $code `
    --user-data-dir $userDataRoot `
    --extensions-dir $extensionsRoot `
    --list-extensions `
    --show-versions
Assert-LastExitCode "VS Code extension listing"

if ($OpenCode) {
    & $code `
        --user-data-dir $userDataRoot `
        --extensions-dir $extensionsRoot `
        --disable-workspace-trust `
        --new-window `
        $projectRoot `
        (Join-Path $projectRoot "src/copeland/Program.ts")
}

Write-Host "Package-only proof directory: $proofRoot"
