[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$exceptionPath = Join-Path $PSScriptRoot "dependency-boundary-exceptions.json"
$exceptions = @(Get-Content -Raw $exceptionPath | ConvertFrom-Json)

function Get-RepositoryRelativePath {
    param([string]$Path)

    return ([IO.Path]::GetRelativePath($repositoryRoot, $Path)).Replace("\", "/")
}

function Get-Subsystem {
    param([string]$RelativePath)

    if ($RelativePath.StartsWith("src/Copeland/")) { return "Copeland" }
    if ($RelativePath.StartsWith("src/Machina.UI/")) { return "Machina.UI" }
    if ($RelativePath.StartsWith("src/Aurelian/")) { return "Aurelian" }
    if ($RelativePath.StartsWith("src/Integrations/")) { return "Integrations" }
    return $null
}

function Test-Exception {
    param(
        [string]$Project,
        [string]$Package
    )

    return @($exceptions | Where-Object {
        $_.project -eq $Project -and $_.package -eq $Package
    }).Count -gt 0
}

function Add-TextDependencyViolations {
    param(
        [string]$ProjectPath,
        [string[]]$ProhibitedTokens
    )

    $projectDirectory = Split-Path -Parent (Join-Path $repositoryRoot $ProjectPath)
    $sourceFiles = @(Get-ChildItem $projectDirectory -Recurse -Filter *.cs)

    foreach ($sourceFile in $sourceFiles) {
        $source = Get-Content -Raw $sourceFile.FullName
        foreach ($token in $ProhibitedTokens) {
            if ($source.Contains($token, [StringComparison]::Ordinal)) {
                $sourcePath = Get-RepositoryRelativePath $sourceFile.FullName
                $violations.Add("$sourcePath contains prohibited dependency token '$token' for $ProjectPath.")
            }
        }
    }
}

function Add-ProjectGraphCycleViolations {
    $projectFiles = @(Get-ChildItem (Join-Path $repositoryRoot "src") -Recurse -Filter *.csproj)
    $projectPaths = @{}
    $dependencies = @{}

    foreach ($projectFile in $projectFiles) {
        $projectPath = Get-RepositoryRelativePath $projectFile.FullName
        $projectPaths[$projectPath] = $projectFile.FullName
        $dependencies[$projectPath] = [Collections.Generic.List[string]]::new()
    }

    foreach ($projectPath in $projectPaths.Keys) {
        [xml]$project = Get-Content -Raw $projectPaths[$projectPath]
        $projectDirectory = Split-Path -Parent $projectPaths[$projectPath]

        foreach ($projectReference in @($project.Project.ItemGroup.ProjectReference)) {
            $targetPath = [IO.Path]::GetFullPath((Join-Path $projectDirectory ([string]$projectReference.Include)))
            $targetRelativePath = Get-RepositoryRelativePath $targetPath
            if ($projectPaths.ContainsKey($targetRelativePath)) {
                $dependencies[$projectPath].Add($targetRelativePath)
            }
        }
    }

    $visiting = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $stack = [Collections.Generic.List[string]]::new()

    function Visit-ProjectGraphNode {
        param([string]$ProjectPath)

        if ($visited.Contains($ProjectPath)) {
            return
        }

        if (-not ($visiting.Add($ProjectPath))) {
            $cycleStart = $stack.IndexOf($ProjectPath)
            $cycle = @($stack.GetRange($cycleStart, $stack.Count - $cycleStart)) + $ProjectPath
            $violations.Add("Aurelian production project graph contains a cycle: $($cycle -join ' -> ')")
            return
        }

        $stack.Add($ProjectPath)
        foreach ($dependency in $dependencies[$ProjectPath]) {
            Visit-ProjectGraphNode $dependency
        }

        $stack.RemoveAt($stack.Count - 1)
        [void]$visiting.Remove($ProjectPath)
        [void]$visited.Add($ProjectPath)
    }

    foreach ($projectPath in $projectPaths.Keys) {
        Visit-ProjectGraphNode $projectPath
    }
}

function Add-SolutionTopologyViolations {
    foreach ($solutionFile in @(Get-ChildItem $repositoryRoot -Filter *.slnx)) {
        [xml]$solution = Get-Content -Raw $solutionFile.FullName
        foreach ($project in @($solution.Solution.Folder.Project)) {
            $projectPath = [string]$project.Path
            $resolvedPath = Join-Path $repositoryRoot $projectPath
            if (-not (Test-Path $resolvedPath -PathType Leaf)) {
                $solutionPath = Get-RepositoryRelativePath $solutionFile.FullName
                $violations.Add("$solutionPath references missing project $projectPath.")
            }
        }
    }

    $fastSolutions = @("Aurelian.slnx", "JointTaskForce.slnx")
    $expensiveProjects = @(
        "tests/Aurelian/Aurelian.Integration.Tests/Aurelian.Integration.Tests.csproj",
        "tests/Aurelian/Aurelian.VisibleTriangle.Tests/Aurelian.VisibleTriangle.Tests.csproj")

    foreach ($solutionName in $fastSolutions) {
        $solutionPath = Join-Path $repositoryRoot $solutionName
        $solutionText = Get-Content -Raw $solutionPath
        foreach ($expensiveProject in $expensiveProjects) {
            if ($solutionText.Contains($expensiveProject, [StringComparison]::Ordinal)) {
                $violations.Add("$solutionName includes expensive integration project $expensiveProject.")
            }
        }
    }
}

$violations = [Collections.Generic.List[string]]::new()
$projects = Get-ChildItem (Join-Path $repositoryRoot "src") -Recurse -Filter *.csproj | Sort-Object FullName

foreach ($projectFile in $projects) {
    $projectPath = Get-RepositoryRelativePath $projectFile.FullName
    $sourceSubsystem = Get-Subsystem $projectPath
    if ($null -eq $sourceSubsystem) { continue }

    [xml]$project = Get-Content -Raw $projectFile.FullName

    foreach ($packageReference in @($project.Project.ItemGroup.PackageReference)) {
        $package = [string]$packageReference.Include
        if ([string]::IsNullOrWhiteSpace($package)) {
            continue
        }

        if ($sourceSubsystem -in @("Copeland", "Machina.UI") -and $package -like "Dominatus.*") {
            if (-not (Test-Exception $projectPath $package)) {
                $violations.Add("$projectPath references prohibited Dominatus package $package without a recorded exception.")
            }
        }

        if ($sourceSubsystem -eq "Aurelian" -and $package -like "Machina*") {
            $violations.Add("$projectPath references prohibited Machina package $package.")
        }

        if ($projectPath -eq "src/Aurelian/Aurelian.Core/Aurelian.Core.csproj" -and $package -like "Silk.NET*") {
            $violations.Add("Aurelian.Core must not reference Silk.NET package $package.")
        }

        if ($projectPath -eq "src/Aurelian/Aurelian.Runtime/Aurelian.Runtime.csproj" -and $package -like "Silk.NET*") {
            $violations.Add("Aurelian.Runtime must not reference Silk.NET package $package.")
        }

        if ($projectPath -eq "src/Aurelian/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj") {
            $violations.Add("Aurelian.Rendering.Contracts must not reference package $package.")
        }
    }

    foreach ($projectReference in @($project.Project.ItemGroup.ProjectReference)) {
        if ([string]::IsNullOrWhiteSpace([string]$projectReference.Include)) {
            continue
        }

        $targetPath = [IO.Path]::GetFullPath((Join-Path $projectFile.DirectoryName ([string]$projectReference.Include)))
        $targetRelativePath = Get-RepositoryRelativePath $targetPath
        $targetSubsystem = Get-Subsystem $targetRelativePath

        if ($targetRelativePath.StartsWith("samples/")) {
            $violations.Add("$projectPath references sample project $targetRelativePath.")
            continue
        }

        if ($sourceSubsystem -eq "Aurelian" -and $targetRelativePath.StartsWith("src/Machina.UI/")) {
            $violations.Add("$projectPath references prohibited Machina project $targetRelativePath.")
        }

        if ($projectPath -eq "src/Aurelian/Aurelian.Core/Aurelian.Core.csproj" -and $targetRelativePath -eq "src/Aurelian/Aurelian.Graphics/Aurelian.Graphics.csproj") {
            $violations.Add("Aurelian.Core must not reference Aurelian.Graphics.")
        }

        if ($projectPath -eq "src/Aurelian/Aurelian.Runtime/Aurelian.Runtime.csproj" -and $targetRelativePath -eq "src/Aurelian/Aurelian.Graphics/Aurelian.Graphics.csproj") {
            $violations.Add("Aurelian.Runtime must not reference Aurelian.Graphics.")
        }

        if ($projectPath -eq "src/Aurelian/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj") {
            $violations.Add("Aurelian.Rendering.Contracts must not reference production project $targetRelativePath.")
        }

        if ($null -eq $targetSubsystem -or $sourceSubsystem -eq $targetSubsystem) {
            continue
        }

        if ($sourceSubsystem -ne "Integrations") {
            $violations.Add("$projectPath has cross-subsystem production reference to $targetRelativePath; use an explicitly named integration project.")
            continue
        }

        if ($targetSubsystem -notin @("Copeland", "Machina.UI", "Aurelian")) {
            $violations.Add("$projectPath references unsupported integration target $targetRelativePath.")
        }
    }
}

Add-TextDependencyViolations "src/Aurelian/Aurelian.Core/Aurelian.Core.csproj" @(
    "Aurelian.Graphics",
    "Silk.NET",
    "Vulkan")
Add-TextDependencyViolations "src/Aurelian/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj" @(
    "Aurelian.Core",
    "Aurelian.Runtime",
    "Dominatus",
    "Machina",
    "Silk.NET",
    "Vulkan")
Add-TextDependencyViolations "src/Aurelian/Aurelian.Runtime/Aurelian.Runtime.csproj" @(
    "Aurelian.Graphics",
    "Silk.NET",
    "Vulkan",
    "Windowing")
Add-ProjectGraphCycleViolations
Add-SolutionTopologyViolations

if ($violations.Count -gt 0) {
    Write-Error ("Dependency boundary validation failed:`n- " + ($violations -join "`n- "))
    exit 1
}

Write-Output "Dependency boundary validation passed for $($projects.Count) production projects."
Write-Output "Recorded temporary exceptions: $($exceptions.Count)."
