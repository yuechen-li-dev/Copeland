[CmdletBinding()]
param(
    [string]$OutputDir = "artifacts\\m7e",
    [switch]$IncludeDirectOutlineTextProof,
    [switch]$IncludeMsdfFontProof,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "samples\\Machina.ComponentGallery.Sample\\Machina.ComponentGallery.Sample.csproj"

if ([System.IO.Path]::IsPathRooted($OutputDir))
{
    $resolvedOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
}
else
{
    $resolvedOutputDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}

New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null

$exports = @(
    @{
        Name = "component-gallery-default"
        Args = @()
    },
    @{
        Name = "component-gallery-interactive"
        Args = @("--primary-clicks", "1", "--checkbox", "on", "--switch", "on")
    }
)

if ($IncludeMsdfFontProof)
{
    $exports += @{
        Name = "component-gallery-msdf-proof"
        Args = @("--include-msdf-font-proof")
    }
}

if ($IncludeDirectOutlineTextProof)
{
    $directOutlineArgs = @("--include-direct-outline-text-proof")

    if ($IncludeMsdfFontProof)
    {
        $directOutlineArgs += "--include-msdf-font-proof"
    }

    $exports += @{
        Name = "component-gallery-direct-outline-text-proof"
        Args = $directOutlineArgs
    }
}

Push-Location $repoRoot

try
{
    foreach ($export in $exports)
    {
        $arguments = @(
            "run",
            "--configuration",
            $Configuration,
            "--project",
            $projectPath,
            "--",
            "--export-only",
            "--export-dir",
            $resolvedOutputDir,
            "--export-name",
            $export.Name
        ) + $export.Args

        & dotnet @arguments

        if ($LASTEXITCODE -ne 0)
        {
            throw "Gallery export failed for $($export.Name)."
        }
    }
}
finally
{
    Pop-Location
}

$createdFiles = @(
    (Join-Path $resolvedOutputDir "component-gallery-default.png"),
    (Join-Path $resolvedOutputDir "component-gallery-interactive.png")
)

if ($IncludeMsdfFontProof)
{
    $createdFiles += (Join-Path $resolvedOutputDir "component-gallery-msdf-proof.png")
}

if ($IncludeDirectOutlineTextProof)
{
    $createdFiles += (Join-Path $resolvedOutputDir "component-gallery-direct-outline-text-proof.png")
    $createdFiles += (Join-Path $resolvedOutputDir "component-gallery-text-backend-comparison.png")
    $createdFiles += (Join-Path $resolvedOutputDir "direct-outline-static-text-proof.png")
}

foreach ($createdFile in $createdFiles)
{
    if (-not (Test-Path -LiteralPath $createdFile))
    {
        throw "Expected export file was not created: $createdFile"
    }
}

Write-Host "Created gallery artifacts:"

foreach ($createdFile in $createdFiles)
{
    Write-Host $createdFile
}
