param(
    [string]$RepositoryRoot = $PSScriptRoot + "\\.."
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path $RepositoryRoot).Path

function Require-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-ProjectReferences {
    param([string]$ProjectPath)

    [xml]$project = Get-Content -LiteralPath $ProjectPath
    $projectDirectory = Split-Path -Parent $ProjectPath

    foreach ($reference in @($project.Project.ItemGroup.ProjectReference)) {
        if ($null -ne $reference -and -not [string]::IsNullOrWhiteSpace($reference.Include)) {
            Join-Path $projectDirectory $reference.Include
        }
    }
}

$solutionPaths = Get-ChildItem -LiteralPath $root -Filter *.slnx -File
foreach ($solutionPath in $solutionPaths) {
    [xml]$solution = Get-Content -LiteralPath $solutionPath.FullName
    foreach ($project in @($solution.Solution.Folder.Project)) {
        $projectPath = Join-Path $root $project.Path
        Require-Condition (Test-Path -LiteralPath $projectPath) "Solution project path does not exist: $($project.Path)"
    }
}

$projects = Get-ChildItem -LiteralPath $root -Recurse -Filter *.csproj -File |
    Where-Object { $_.FullName -notmatch '\\reference\\' }
$projectReferences = @{}

foreach ($project in $projects) {
    $references = @()
    foreach ($reference in Get-ProjectReferences $project.FullName) {
        $resolvedReference = [System.IO.Path]::GetFullPath($reference)
        Require-Condition (Test-Path -LiteralPath $resolvedReference) "Project reference does not exist: $resolvedReference"
        $references += $resolvedReference
    }

    $projectReferences[$project.FullName] = $references
}

$visited = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$active = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

function Test-ProjectCycle {
    param([string]$ProjectPath)

    if ($active.Contains($ProjectPath)) {
        throw "Project graph cycle detected at $ProjectPath"
    }

    if ($visited.Contains($ProjectPath)) {
        return
    }

    $active.Add($ProjectPath) | Out-Null
    foreach ($reference in $projectReferences[$ProjectPath]) {
        if ($projectReferences.ContainsKey($reference)) {
            Test-ProjectCycle $reference
        }
    }

    $active.Remove($ProjectPath) | Out-Null
    $visited.Add($ProjectPath) | Out-Null
}

foreach ($project in $projects) {
    Test-ProjectCycle $project.FullName
}

$mirProject = Join-Path $root "src/Copeland/Copeland.TS.Mir/Copeland.TS.Mir.csproj"
Require-Condition ($projectReferences[$mirProject].Count -eq 0) "Copeland.TS.Mir must not reference another project."

$frontendProject = Join-Path $root "src/Copeland/Copeland.TS/Copeland.TS.csproj"
$backendProject = Join-Path $root "src/Copeland/Copeland.TS.Backend.CSharp/Copeland.TS.Backend.CSharp.csproj"
Require-Condition ($projectReferences[$frontendProject] -contains $mirProject) "Copeland.TS must reference Copeland.TS.Mir."
Require-Condition ($projectReferences[$backendProject] -contains $mirProject) "Copeland.TS.Backend.CSharp must reference Copeland.TS.Mir."
Require-Condition ($projectReferences[$backendProject] -notcontains $frontendProject) "The C# backend must not reference the frontend."

$aurelianShaderTests = Join-Path $root "tests/Aurelian/Aurelian.Shaders.Tests/Aurelian.Shaders.Tests.csproj"
$aurelianReferences = $projectReferences[$aurelianShaderTests]
Require-Condition (-not ($aurelianReferences | Where-Object { $_ -match 'Copeland\\Copeland\.(TS|Markdown)' })) "Aurelian shader tests must remain independent of Copeland TS and Markdown."

$oldPaths = Get-ChildItem -LiteralPath $root -Recurse -File -Include *.cs,*.csproj,*.slnx,*.ps1 |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|reference)\\' } |
    Where-Object { $_.Name -ne 'Validate-CopelandTsTopology.ps1' } |
    Select-String -Pattern 'Copeland\.Script' -SimpleMatch
Require-Condition ($null -eq $oldPaths) "Copeland.Script remains in active source, project, solution, or validation files."

$copeTestFiles = Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|reference)\\' } |
    Where-Object { $_.Name -match 'cope-test-v0' }
Require-Condition ($copeTestFiles.Count -eq 0) "An abandoned cope-test-v0 file remains."

$forbiddenAbstractions = Get-ChildItem -LiteralPath (Join-Path $root "src/Copeland") -Recurse -Filter *.cs -File |
    Select-String -Pattern 'IBackend|ICompilerBackend|IIntermediateRepresentation|ICompilerPass'
Require-Condition ($null -eq $forbiddenAbstractions) "A forbidden universal compiler abstraction was introduced."

$copeFixtures = Get-ChildItem -LiteralPath (Join-Path $root "tests/Copeland/Copeland.TS.Tests/TestData/Corpus") -Recurse -Filter *.cope -File
foreach ($fixture in $copeFixtures) {
    Require-Condition ($fixture.Directory.Name -match 'mir') ".cope fixture is not owned by a MIR corpus case: $($fixture.FullName)"
}

Write-Output "Copeland TS topology validation passed."
