[CmdletBinding()]
param(
    [string]$OutputPath = "artifacts\\presenter-default.png",
    [switch]$IncludeDirectOutlineRenderBridgeProof,
    [switch]$IncludeNavigationShell,
    [string]$SelectedSection,
    [string]$SelectedTab,
    [string]$NavigationPage,
    [string]$ScrollPage,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "samples\\Machina.Presenter.Sample\\Machina.Presenter.Sample.csproj"

if ([System.IO.Path]::IsPathRooted($OutputPath))
{
    $resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
}
else
{
    $resolvedOutputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath))
}

$outputDirectory = Split-Path -Parent $resolvedOutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory))
{
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$arguments = @(
    "run",
    "--configuration",
    $Configuration,
    "--project",
    $projectPath,
    "--",
    "--export-only",
    "--output-path",
    $resolvedOutputPath
)

if ($IncludeDirectOutlineRenderBridgeProof)
{
    $arguments += "--include-direct-outline-render-bridge-proof"
}

if ($IncludeNavigationShell)
{
    $arguments += "--include-navigation-shell"
}

if (-not [string]::IsNullOrWhiteSpace($NavigationPage))
{
    $arguments += "--navigation-page"
    $arguments += $NavigationPage
}

if (-not [string]::IsNullOrWhiteSpace($SelectedSection))
{
    $arguments += "--selected-section"
    $arguments += $SelectedSection
}

if (-not [string]::IsNullOrWhiteSpace($SelectedTab))
{
    $arguments += "--selected-tab"
    $arguments += $SelectedTab
}

if (-not [string]::IsNullOrWhiteSpace($ScrollPage))
{
    $arguments += "--scroll-page"
    $arguments += $ScrollPage
}

Push-Location $repoRoot

try
{
    & dotnet @arguments

    if ($LASTEXITCODE -ne 0)
    {
        throw "Presenter export failed."
    }
}
finally
{
    Pop-Location
}

if (-not (Test-Path -LiteralPath $resolvedOutputPath))
{
    throw "Expected presenter export file was not created: $resolvedOutputPath"
}

Write-Host "Created presenter artifact:"
Write-Host $resolvedOutputPath
