[CmdletBinding()]
param(
    [string]$OutputDir = "artifacts\\m9d",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string[]]$Preset = @("direct-vs-msdf", "cad-debug"),
    [ValidateSet("DirectOutlineStatic", "MsdfScalableExperimental")]
    [string]$TextBackend = "DirectOutlineStatic",
    [switch]$ShowGrid,
    [int]$GridStep = 8,
    [switch]$ShowUnitLabels,
    [switch]$ShowAxes,
    [int]$AxisStep = 32,
    [switch]$ShowBounds,
    [switch]$ShowWireframe,
    [switch]$Clean,
    [switch]$AllowPartial
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "tests\\Machina.Fonts.Tooling.Tests\\Machina.Fonts.Tooling.Tests.csproj"
$filter = "FullyQualifiedName~Machina.Fonts.Tooling.Tests.FontDiagnosticExportSmokeTests.ScriptSmoke_FontDiagnosticsExport_ExportsArtifacts"

if ([System.IO.Path]::IsPathRooted($OutputDir))
{
    $resolvedOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
}
else
{
    $resolvedOutputDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}

Push-Location $repoRoot

try
{
    $effectiveShowGrid = if ($PSBoundParameters.ContainsKey("ShowGrid")) { $ShowGrid.IsPresent } else { $true }
    $effectiveShowUnitLabels = if ($PSBoundParameters.ContainsKey("ShowUnitLabels")) { $ShowUnitLabels.IsPresent } else { $true }
    $effectiveShowAxes = if ($PSBoundParameters.ContainsKey("ShowAxes")) { $ShowAxes.IsPresent } else { $true }
    $effectiveShowBounds = if ($PSBoundParameters.ContainsKey("ShowBounds")) { $ShowBounds.IsPresent } else { $true }
    $effectiveShowWireframe = if ($PSBoundParameters.ContainsKey("ShowWireframe")) { $ShowWireframe.IsPresent } else { $true }

    $env:MACHINA_FONT_DIAGNOSTICS_OUTPUT_DIR = $resolvedOutputDir
    $env:MACHINA_FONT_DIAGNOSTICS_PRESET = ($Preset -join ",")
    $env:MACHINA_FONT_DIAGNOSTICS_SHOW_GRID = $effectiveShowGrid.ToString().ToLowerInvariant()
    $env:MACHINA_FONT_DIAGNOSTICS_GRID_STEP = $GridStep.ToString()
    $env:MACHINA_FONT_DIAGNOSTICS_SHOW_UNIT_LABELS = $effectiveShowUnitLabels.ToString().ToLowerInvariant()
    $env:MACHINA_FONT_DIAGNOSTICS_SHOW_AXES = $effectiveShowAxes.ToString().ToLowerInvariant()
    $env:MACHINA_FONT_DIAGNOSTICS_AXIS_STEP = $AxisStep.ToString()
    $env:MACHINA_FONT_DIAGNOSTICS_SHOW_BOUNDS = $effectiveShowBounds.ToString().ToLowerInvariant()
    $env:MACHINA_FONT_DIAGNOSTICS_SHOW_WIREFRAME = $effectiveShowWireframe.ToString().ToLowerInvariant()
    $env:MACHINA_FONT_DIAGNOSTICS_CLEAN = $Clean.IsPresent.ToString().ToLowerInvariant()
    $env:MACHINA_FONT_DIAGNOSTICS_ALLOW_PARTIAL = $AllowPartial.IsPresent.ToString().ToLowerInvariant()
    $env:MACHINA_FONT_DIAGNOSTICS_TEXT_BACKEND = $TextBackend
    $env:MACHINA_FONT_DIAGNOSTICS_REPO_ROOT = $repoRoot.Path

    if (-not $Clean.IsPresent -and (Test-Path -LiteralPath $resolvedOutputDir))
    {
        $existingEntries = Get-ChildItem -LiteralPath $resolvedOutputDir -Force -ErrorAction SilentlyContinue
        if ($null -ne $existingEntries -and $existingEntries.Count -gt 0)
        {
            Write-Warning "Output directory '$resolvedOutputDir' already contains files. Existing artifacts may be overwritten and stale files may remain. Use -Clean for repeated local/Codex runs."
        }
    }

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
    Remove-Item Env:\MACHINA_FONT_DIAGNOSTICS_CLEAN -ErrorAction SilentlyContinue
    Remove-Item Env:\MACHINA_FONT_DIAGNOSTICS_ALLOW_PARTIAL -ErrorAction SilentlyContinue
    Remove-Item Env:\MACHINA_FONT_DIAGNOSTICS_TEXT_BACKEND -ErrorAction SilentlyContinue
    Remove-Item Env:\MACHINA_FONT_DIAGNOSTICS_REPO_ROOT -ErrorAction SilentlyContinue
    Pop-Location
}

Write-Host "Created Machina M9d font diagnostics artifacts:"
Get-ChildItem -LiteralPath $resolvedOutputDir -Recurse | Sort-Object FullName | ForEach-Object {
    Write-Host $_.FullName
}
