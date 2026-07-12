[CmdletBinding()]
param(
    [string]$OutputDir = "artifacts\\m12a",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "src\\Copeland\\Copeland.Cli\\Copeland.Cli.csproj"

if ([System.IO.Path]::IsPathRooted($OutputDir))
{
    $resolvedOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
}
else
{
    $resolvedOutputDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}

New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null

$arguments = @(
    "run",
    "--configuration",
    $Configuration,
    "--project",
    $projectPath,
    "--",
    "markdown",
    "export-corpus",
    "--output-dir",
    $resolvedOutputDir
)

Push-Location $repoRoot

try
{
    & dotnet @arguments

    if ($LASTEXITCODE -ne 0)
    {
        throw "Copeland Markdown corpus export failed."
    }
}
finally
{
    Pop-Location
}

Write-Host "Created Copeland Markdown artifacts:"
Write-Host $resolvedOutputDir
