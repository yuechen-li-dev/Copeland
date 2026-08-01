[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot "..")
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path $RepositoryRoot).Path
$releaseSolution = Join-Path $repositoryRoot "Copeland.Release.slnx"

function Require-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-NormalizedPath {
    param([string]$Path)

    return [IO.Path]::GetFullPath($Path)
}

function Get-ProjectReferences {
    param([string]$ProjectPath)

    [xml]$project = Get-Content -LiteralPath $ProjectPath
    $projectDirectory = Split-Path -Parent $ProjectPath
    foreach ($reference in @($project.SelectNodes('//ProjectReference'))) {
        if (-not [string]::IsNullOrWhiteSpace($reference.Include)) {
            Get-NormalizedPath (Join-Path $projectDirectory $reference.Include)
        }
    }
}

Require-Condition (Test-Path -LiteralPath $releaseSolution -PathType Leaf) "Release solution is missing: $releaseSolution"
[xml]$solution = Get-Content -LiteralPath $releaseSolution
$solutionProjects = @($solution.SelectNodes('//Project') | ForEach-Object {
    Get-NormalizedPath (Join-Path $repositoryRoot $_.Path)
})
$solutionProjectSet = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($project in $solutionProjects) {
    Require-Condition (Test-Path -LiteralPath $project -PathType Leaf) "Release solution project is missing: $project"
    $solutionProjectSet.Add($project) | Out-Null
}

$packageRoots = @(
    "src/Copeland/Copeland.Cli/Copeland.Cli.csproj",
    "src/Copeland/Copeland.TS.MSBuild/Copeland.TS.MSBuild.csproj",
    "src/Copeland/Copeland.TS.Templates/Copeland.TS.Templates.csproj"
) | ForEach-Object { Get-NormalizedPath (Join-Path $repositoryRoot $_) }

$visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Assert-PackageClosure {
    param([string]$ProjectPath)

    if (-not $visited.Add($ProjectPath)) {
        return
    }

    Require-Condition ($solutionProjectSet.Contains($ProjectPath)) "Release package closure omits '$ProjectPath'. Add it to Copeland.Release.slnx."
    foreach ($reference in Get-ProjectReferences $ProjectPath) {
        Require-Condition (Test-Path -LiteralPath $reference -PathType Leaf) "Package project reference is missing: $reference"
        Assert-PackageClosure $reference
    }
}

foreach ($packageRoot in $packageRoots) {
    Assert-PackageClosure $packageRoot
}

$requiredTests = @(
    "tests/Copeland/Copeland.Cli.Tests/Copeland.Cli.Tests.csproj",
    "tests/Copeland/Copeland.TS.Tests/Copeland.TS.Tests.csproj",
    "tests/Copeland/Copeland.TS.Database.Tests/Copeland.TS.Database.Tests.csproj",
    "tests/Copeland/Copeland.TS.Backend.CSharp.Tests/Copeland.TS.Backend.CSharp.Tests.csproj",
    "tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/Copeland.TS.Backend.JavaScript.Tests.csproj",
    "tests/Copeland/Copeland.TS.MSBuild.Tests/Copeland.TS.MSBuild.Tests.csproj"
) | ForEach-Object { Get-NormalizedPath (Join-Path $repositoryRoot $_) }
foreach ($testProject in $requiredTests) {
    Require-Condition ($solutionProjectSet.Contains($testProject)) "Release-authoritative test is omitted: $testProject"
}

$excludedPrerequisiteProjects = @(
    "samples/copeland-ts/tsxml-react-m0/Copeland.TsXml.React.M0.csproj",
    "samples/copeland-ts/tsxml-react-m0/Host/Copeland.ReactClr.M0.Host.csproj",
    "samples/copeland-ts/standalone-web-m0/Generate/StandaloneWebM0.Generate.csproj",
    "samples/copeland-ts/standalone-web-m0/StandaloneWebM0.csproj"
) | ForEach-Object { Get-NormalizedPath (Join-Path $repositoryRoot $_) }
foreach ($excludedProject in $excludedPrerequisiteProjects) {
    Require-Condition (-not $solutionProjectSet.Contains($excludedProject)) "Prerequisite-bound browser/TSPack project is in the release closure: $excludedProject"
}

$npmLauncher = Join-Path $repositoryRoot "src\Copeland\Copeland.TS.Npm\launcher\tscl.js"
Require-Condition (Test-Path -LiteralPath $npmLauncher -PathType Leaf) "npm launcher source is missing from the release closure: $npmLauncher"

Write-Output "Copeland Preview release product closure validation passed."
Write-Output "Release solution: Copeland.Release.slnx"
$packageRootNames = ($packageRoots | ForEach-Object { Split-Path $_ -Leaf }) -join ', '
$excludedProjectNames = ($excludedPrerequisiteProjects | ForEach-Object { Split-Path $_ -Leaf }) -join ', '
Write-Output "Package roots: $packageRootNames"
Write-Output "Excluded prerequisite-bound projects: $excludedProjectNames"
