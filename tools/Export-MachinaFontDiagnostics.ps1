[CmdletBinding()]
param(
    [string]$OutputDir = "artifacts\\m9b",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string[]]$Preset = @("direct-vs-msdf", "cad-debug"),
    [switch]$ShowGrid,
    [int]$GridStep = 8,
    [switch]$ShowUnitLabels,
    [switch]$ShowAxes,
    [int]$AxisStep = 32,
    [switch]$ShowBounds,
    [switch]$ShowWireframe
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "tests\\Machina.Fonts.Tooling.Tests\\Machina.Fonts.Tooling.Tests.csproj"
$filter = "FullyQualifiedName~Machina.Fonts.Tooling.Tests.FontDiagnosticExportTests.FontDiagnosticsExport_ScriptWorkflowExportsArtifacts"

if ([System.IO.Path]::IsPathRooted($OutputDir))
{
    $resolvedOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
}
else
{
    $resolvedOutputDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}

New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null

Push-Location $repoRoot

try
{
    $effectiveShowGrid = $PSBoundParameters.ContainsKey("ShowGrid") ? $ShowGrid.IsPresent : $true
    $effectiveShowUnitLabels = $PSBoundParameters.ContainsKey("ShowUnitLabels") ? $ShowUnitLabels.IsPresent : $true
    $effectiveShowAxes = $PSBoundParameters.ContainsKey("ShowAxes") ? $ShowAxes.IsPresent : $true
    $effectiveShowBounds = $PSBoundParameters.ContainsKey("ShowBounds") ? $ShowBounds.IsPresent : $true
    $effectiveShowWireframe = $PSBoundParameters.ContainsKey("ShowWireframe") ? $ShowWireframe.IsPresent : $true

    $env:MACHINA_FONT_DIAGNOSTICS_OUTPUT_DIR = $resolvedOutputDir
    $env:MACHINA_FONT_DIAGNOSTICS_PRESET = ($Preset -join ",")
    $env:MACHINA_FONT_DIAGNOSTICS_SHOW_GRID = $effectiveShowGrid.ToString().ToLowerInvariant()
    $env:MACHINA_FONT_DIAGNOSTICS_GRID_STEP = $GridStep.ToString()
    $env:MACHINA_FONT_DIAGNOSTICS_SHOW_UNIT_LABELS = $effectiveShowUnitLabels.ToString().ToLowerInvariant()
    $env:MACHINA_FONT_DIAGNOSTICS_SHOW_AXES = $effectiveShowAxes.ToString().ToLowerInvariant()
    $env:MACHINA_FONT_DIAGNOSTICS_AXIS_STEP = $AxisStep.ToString()
    $env:MACHINA_FONT_DIAGNOSTICS_SHOW_BOUNDS = $effectiveShowBounds.ToString().ToLowerInvariant()
    $env:MACHINA_FONT_DIAGNOSTICS_SHOW_WIREFRAME = $effectiveShowWireframe.ToString().ToLowerInvariant()

    $arguments = @(
        "test",
        $projectPath,
        "--configuration",
        $Configuration,
        "--filter",
        $filter
    )

    & dotnet @arguments

    if ($LASTEXITCODE -ne 0)
    {
        throw "Machina font diagnostics export failed."
    }
}
finally
{
    Remove-Item Env:\MACHINA_FONT_DIAGNOSTICS_OUTPUT_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:\MACHINA_FONT_DIAGNOSTICS_PRESET -ErrorAction SilentlyContinue
    Remove-Item Env:\MACHINA_FONT_DIAGNOSTICS_SHOW_GRID -ErrorAction SilentlyContinue
    Remove-Item Env:\MACHINA_FONT_DIAGNOSTICS_GRID_STEP -ErrorAction SilentlyContinue
    Remove-Item Env:\MACHINA_FONT_DIAGNOSTICS_SHOW_UNIT_LABELS -ErrorAction SilentlyContinue
    Remove-Item Env:\MACHINA_FONT_DIAGNOSTICS_SHOW_AXES -ErrorAction SilentlyContinue
    Remove-Item Env:\MACHINA_FONT_DIAGNOSTICS_AXIS_STEP -ErrorAction SilentlyContinue
    Remove-Item Env:\MACHINA_FONT_DIAGNOSTICS_SHOW_BOUNDS -ErrorAction SilentlyContinue
    Remove-Item Env:\MACHINA_FONT_DIAGNOSTICS_SHOW_WIREFRAME -ErrorAction SilentlyContinue
    Pop-Location
}

Write-Host "Created Machina M9b font diagnostics artifacts:"
Get-ChildItem -LiteralPath $resolvedOutputDir -Recurse | Sort-Object FullName | ForEach-Object {
    Write-Host $_.FullName
}
