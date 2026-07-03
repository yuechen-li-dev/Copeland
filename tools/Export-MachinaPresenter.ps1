[CmdletBinding()]
param(
    [string]$OutputPath = "artifacts\\presenter-default.png",
    [switch]$IncludeDirectOutlineRenderBridgeProof,
    [switch]$IncludeNavigationShell,
    [switch]$LegacySingleCard,
    [string]$OblivionWorkspace,
    [string]$SelectedSection,
    [string]$SelectedTab,
    [string]$SelectedCard,
    [string]$ExpandedCard,
    [double]$ExpandedCardBodyScroll,
    [double]$InspectorScroll,
    [double]$InspectorRawSourceScroll,
    [ValidateSet("CardList", "Inspector")]
    [string]$CompactPane,
    [ValidateSet("Wide", "Compact")]
    [string]$ShellMode,
    [int]$Width = 1120,
    [int]$Height = 760,
    [string]$InvokeAction,
    [string]$NavigationPage,
    [string]$ScrollPage,
    [string]$PlaybackScenario,
    [string]$PlaybackSuite,
    [string]$OutputDirectory = "artifacts\\m16c\\playback",
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

$resolvedOutputPathDirectory = Split-Path -Parent $resolvedOutputPath
if (-not [string]::IsNullOrWhiteSpace($resolvedOutputPathDirectory))
{
    New-Item -ItemType Directory -Path $resolvedOutputPathDirectory -Force | Out-Null
}

if ([System.IO.Path]::IsPathRooted($OutputDirectory))
{
    $resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
}
else
{
    $resolvedOutputDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
}

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

$arguments = @(
    "run",
    "--configuration",
    $Configuration,
    "--project",
    $projectPath,
    "--",
    "--export-only",
    "--output-path",
    $resolvedOutputPath,
    "--width",
    $Width,
    "--height",
    $Height
)

if ($IncludeDirectOutlineRenderBridgeProof)
{
    $arguments += "--include-direct-outline-render-bridge-proof"
}

if ($IncludeNavigationShell)
{
    $arguments += "--include-navigation-shell"
}

if ($LegacySingleCard)
{
    $arguments += "--legacy-single-card"
}

if (-not [string]::IsNullOrWhiteSpace($OblivionWorkspace))
{
    $arguments += "--oblivion-workspace"
    $arguments += $OblivionWorkspace
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

if (-not [string]::IsNullOrWhiteSpace($SelectedCard))
{
    $arguments += "--selected-card"
    $arguments += $SelectedCard
}

if (-not [string]::IsNullOrWhiteSpace($ExpandedCard))
{
    $arguments += "--expanded-card"
    $arguments += $ExpandedCard
}

if ($PSBoundParameters.ContainsKey("ExpandedCardBodyScroll"))
{
    $arguments += "--expanded-card-body-scroll"
    $arguments += $ExpandedCardBodyScroll.ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

if ($PSBoundParameters.ContainsKey("InspectorScroll"))
{
    $arguments += "--inspector-scroll"
    $arguments += $InspectorScroll.ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

if ($PSBoundParameters.ContainsKey("InspectorRawSourceScroll"))
{
    $arguments += "--inspector-raw-source-scroll"
    $arguments += $InspectorRawSourceScroll.ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

if (-not [string]::IsNullOrWhiteSpace($CompactPane))
{
    $arguments += "--compact-pane"
    $arguments += $CompactPane
}

if (-not [string]::IsNullOrWhiteSpace($ShellMode))
{
    $arguments += "--shell-mode"
    $arguments += $ShellMode
}

if (-not [string]::IsNullOrWhiteSpace($InvokeAction))
{
    $arguments += "--invoke-action"
    $arguments += $InvokeAction
}

if (-not [string]::IsNullOrWhiteSpace($ScrollPage))
{
    $arguments += "--scroll-page"
    $arguments += $ScrollPage
}

if (-not [string]::IsNullOrWhiteSpace($PlaybackScenario))
{
    $arguments += "--playback-scenario"
    $arguments += $PlaybackScenario
}

if (-not [string]::IsNullOrWhiteSpace($PlaybackSuite))
{
    $arguments += "--playback-suite"
    $arguments += $PlaybackSuite
    $arguments += "--output-directory"
    $arguments += $resolvedOutputDirectory
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

if (-not [string]::IsNullOrWhiteSpace($PlaybackSuite))
{
    $suiteReportJson = Join-Path (Split-Path -Parent $resolvedOutputDirectory) "playback-suite-report.json"
    if (-not (Test-Path -LiteralPath $suiteReportJson))
    {
        throw "Expected presenter playback suite report was not created: $suiteReportJson"
    }

    Write-Host "Created presenter playback suite artifacts:"
    Write-Host $resolvedOutputDirectory
    Write-Host $suiteReportJson
    return
}

if (-not (Test-Path -LiteralPath $resolvedOutputPath))
{
    throw "Expected presenter export file was not created: $resolvedOutputPath"
}

Write-Host "Created presenter artifact:"
Write-Host $resolvedOutputPath
