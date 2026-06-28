[CmdletBinding()]
param(
    [string]$OutputDir = "artifacts\\m8l",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "tests\\Machina.Fonts.Tests\\Machina.Fonts.Tests.csproj"
$filter = "FullyQualifiedName~Machina.Fonts.Tests.Rendering.FontProofExporterTests.FontProofExporter_ScriptWorkflowExportsProofSet"

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
    $env:MACHINA_FONT_PROOF_OUTPUT_DIR = $resolvedOutputDir

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
        throw "Font proof export failed."
    }
}
finally
{
    Remove-Item Env:\MACHINA_FONT_PROOF_OUTPUT_DIR -ErrorAction SilentlyContinue
    Pop-Location
}

$createdFiles = @(
    (Join-Path $resolvedOutputDir "msdf-machina.ppm"),
    (Join-Path $resolvedOutputDir "msdf-aa0.ppm"),
    (Join-Path $resolvedOutputDir "msdf-a-space-a.ppm"),
    (Join-Path $resolvedOutputDir "msdf-machina-0.ppm"),
    (Join-Path $resolvedOutputDir "msdf-hello-machina.ppm"),
    (Join-Path $resolvedOutputDir "msdf-av-to-wa.ppm"),
    (Join-Path $resolvedOutputDir "msdf-spacing-proof.ppm")
)

foreach ($createdFile in $createdFiles)
{
    if (-not (Test-Path -LiteralPath $createdFile))
    {
        throw "Expected proof artifact was not created: $createdFile"
    }
}

Write-Host "Created Machina font proof artifacts:"

foreach ($createdFile in $createdFiles)
{
    Write-Host $createdFile
}
