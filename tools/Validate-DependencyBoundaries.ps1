[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Get-RepositoryRelativePath {
    param([string]$Path)

    $rootWithSeparator = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $rootUri = [Uri]::new($rootWithSeparator)
    $pathUri = [Uri]::new([IO.Path]::GetFullPath($Path))
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace("\", "/")
}

function Get-Subsystem {
    param([string]$RelativePath)

    if ($RelativePath.StartsWith("src/Copeland/")) { return "Copeland" }
    if ($RelativePath.StartsWith("src/Machina.UI/")) { return "Machina.UI" }
    if ($RelativePath.StartsWith("src/Aurelian/")) { return "Aurelian" }
    if ($RelativePath.StartsWith("src/Integrations/")) { return "Integrations" }
    return $null
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

function Add-DominatusOwnershipViolations {
    $retiredProjectPaths = @(
        "src/Machina.UI/Machina.Dominatus/Machina.Dominatus.csproj",
        "tests/Machina.UI/Machina.Dominatus.Tests/Machina.Dominatus.Tests.csproj")

    foreach ($retiredProjectPath in $retiredProjectPaths) {
        if (Test-Path (Join-Path $repositoryRoot $retiredProjectPath)) {
            $violations.Add("Retired Machina Dominatus path remains: $retiredProjectPath.")
        }
    }

    $exceptionManifest = Join-Path $repositoryRoot "tools/dependency-boundary-exceptions.json"
    if (Test-Path $exceptionManifest -PathType Leaf) {
        $violations.Add("Dependency-boundary exceptions are retired; remove tools/dependency-boundary-exceptions.json.")
    }

    $adapterProjectPath = "src/Integrations/Machina.Dominatus/Machina.Dominatus.csproj"
    $adapterProjectFile = Join-Path $repositoryRoot $adapterProjectPath
    if (-not (Test-Path $adapterProjectFile -PathType Leaf)) {
        $violations.Add("The optional Machina Dominatus integration adapter is missing at $adapterProjectPath.")
    } else {
        [xml]$adapterProject = Get-Content -Raw $adapterProjectFile
        $allowedAdapterReferences = @(
            "src/Machina.UI/Machina.Core/Machina.Core.csproj",
            "src/Machina.UI/Machina.Layout/Machina.Layout.csproj",
            "src/Machina.UI/Machina.Presentation/Machina.Presentation.csproj",
            "src/Machina.UI/Machina.Standard/Machina.Standard.csproj")

        foreach ($projectReference in @($adapterProject.Project.ItemGroup.ProjectReference)) {
            $targetPath = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $adapterProjectFile) ([string]$projectReference.Include)))
            $targetRelativePath = Get-RepositoryRelativePath $targetPath
            if ($targetRelativePath -notin $allowedAdapterReferences) {
                $violations.Add("$adapterProjectPath may reference only Machina contracts; found $targetRelativePath.")
            }
        }

        $adapterPackages = @($adapterProject.Project.ItemGroup.PackageReference | ForEach-Object { [string]$_.Include })
        foreach ($requiredPackage in @("Dominatus.Core", "Dominatus.OptFlow")) {
            if ($requiredPackage -notin $adapterPackages) {
                $violations.Add("$adapterProjectPath must retain its explicit $requiredPackage integration dependency.")
            }
        }
    }

    $machinaSourceRoot = Join-Path $repositoryRoot "src/Machina.UI"
    foreach ($sourceFile in @(Get-ChildItem $machinaSourceRoot -Recurse -Filter *.cs | Where-Object {
            $_.FullName -notmatch "\\(bin|obj)\\"
        })) {
        $source = Get-Content -Raw $sourceFile.FullName
        if ($source.Contains("Dominatus", [StringComparison]::Ordinal)) {
            $violations.Add("$(Get-RepositoryRelativePath $sourceFile.FullName) retains a prohibited Dominatus source dependency in Machina production.")
        }
    }

    foreach ($projectFile in @(Get-ChildItem (Join-Path $repositoryRoot "src/Machina.UI") -Recurse -Filter *.csproj)) {
        [xml]$project = Get-Content -Raw $projectFile.FullName
        foreach ($packageReference in @($project.Project.ItemGroup.PackageReference)) {
            if ([string]$packageReference.Include -in @("Dominatus.Core", "Dominatus.OptFlow")) {
                $violations.Add("$(Get-RepositoryRelativePath $projectFile.FullName) references prohibited Dominatus package $([string]$packageReference.Include).")
            }
        }
    }

    foreach ($projectFile in @(Get-ChildItem (Join-Path $repositoryRoot "samples") -Recurse -Filter *.csproj)) {
        $projectText = Get-Content -Raw $projectFile.FullName
        if (($projectText.Contains("ProjectReference", [StringComparison]::Ordinal) -and
                $projectText.Contains("Machina.Dominatus", [StringComparison]::Ordinal)) -or
            $projectText.Contains("Dominatus.Core", [StringComparison]::Ordinal) -or
            $projectText.Contains("Dominatus.OptFlow", [StringComparison]::Ordinal)) {
            $violations.Add("$(Get-RepositoryRelativePath $projectFile.FullName) retains a stale Dominatus sample dependency.")
        }
    }

    foreach ($projectFile in @(Get-ChildItem $repositoryRoot -Recurse -Filter *.csproj)) {
        $projectText = Get-Content -Raw $projectFile.FullName
        if ($projectText.Contains("src/Machina.UI/Machina.Dominatus", [StringComparison]::Ordinal)) {
            $violations.Add("$(Get-RepositoryRelativePath $projectFile.FullName) references the retired Machina-owned Dominatus path.")
        }
    }

    foreach ($solutionFile in @(Get-ChildItem $repositoryRoot -Filter *.slnx)) {
        $solutionText = Get-Content -Raw $solutionFile.FullName
        if ($solutionText.Contains("src/Machina.UI/Machina.Dominatus", [StringComparison]::Ordinal) -or
            $solutionText.Contains("tests/Machina.UI/Machina.Dominatus.Tests", [StringComparison]::Ordinal)) {
            $violations.Add("$(Get-RepositoryRelativePath $solutionFile.FullName) retains a retired Machina-owned Dominatus path.")
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
        "tests/Aurelian/Aurelian.VisibleTriangle.Tests/Aurelian.VisibleTriangle.Tests.csproj",
        "tests/Integrations/Aurelian.Machina.Tests/Aurelian.Machina.Tests.csproj")

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

function Add-IntegrationOwnershipViolations {
    $aurelianTestProjects = @(Get-ChildItem (Join-Path $repositoryRoot "tests/Aurelian") -Recurse -Filter *.csproj)

    foreach ($testProject in $aurelianTestProjects) {
        [xml]$project = Get-Content -Raw $testProject.FullName
        $testProjectPath = Get-RepositoryRelativePath $testProject.FullName

        foreach ($projectReference in @($project.Project.ItemGroup.ProjectReference)) {
            $targetPath = [IO.Path]::GetFullPath((Join-Path $testProject.DirectoryName ([string]$projectReference.Include)))
            $targetRelativePath = Get-RepositoryRelativePath $targetPath

            if ($targetRelativePath.StartsWith("src/Machina.UI/") -or
                $targetRelativePath.StartsWith("src/Integrations/")) {
                $violations.Add("$testProjectPath is Aurelian-owned test coverage but references cross-system project $targetRelativePath; move the coverage to tests/Integrations.")
            }
        }
    }

    $legacyParityPath = Join-Path $repositoryRoot "tests/Aurelian/Aurelian.Integration.Tests/AurelianCpuRasterParityTests.cs"
    if (Test-Path $legacyParityPath -PathType Leaf) {
        $violations.Add("Cross-system AurelianCpuRasterParityTests must live under tests/Integrations, not tests/Aurelian.")
    }

    $testProjects = @(Get-ChildItem (Join-Path $repositoryRoot "tests") -Recurse -Filter *.csproj)
    foreach ($testProject in $testProjects) {
        [xml]$project = Get-Content -Raw $testProject.FullName
        $testProjectPath = Get-RepositoryRelativePath $testProject.FullName
        $referencesMachina = $false
        $referencesAurelian = $false

        foreach ($projectReference in @($project.Project.ItemGroup.ProjectReference)) {
            $targetPath = [IO.Path]::GetFullPath((Join-Path $testProject.DirectoryName ([string]$projectReference.Include)))
            $targetRelativePath = Get-RepositoryRelativePath $targetPath
            $referencesMachina = $referencesMachina -or $targetRelativePath.StartsWith("src/Machina.UI/")
            $referencesAurelian = $referencesAurelian -or
                $targetRelativePath.StartsWith("src/Aurelian/") -or
                $targetRelativePath.StartsWith("samples/Aurelian/")
        }

        if ($referencesMachina -and $referencesAurelian -and -not $testProjectPath.StartsWith("tests/Integrations/")) {
            $violations.Add("$testProjectPath composes Machina and Aurelian coverage; move it under tests/Integrations.")
        }
    }
}

function Add-ScreenOwnershipViolations {
    $screenRoot = "src/Machina.UI/Machina.Presentation/Screens/"
    $requiredScreenFiles = @(
        "IPresenterScreen.cs",
        "PresenterScreenId.cs",
        "PresenterScreenStack.cs",
        "Layer.cs",
        "ScreenLayerKey.cs",
        "ScreenLayerOrder.cs",
        "ScreenLayerSlot.cs",
        "ScreenLayers.cs")

    foreach ($fileName in $requiredScreenFiles) {
        if (-not (Test-Path (Join-Path $repositoryRoot "$screenRoot$fileName") -PathType Leaf)) {
            $violations.Add("Machina presentation screen contract is missing $screenRoot$fileName.")
        }
    }

    $genericScreenDeclarations = @(
        "interface IPresenterScreen",
        "class PresenterScreenStack",
        "struct PresenterScreenId",
        "struct ScreenLayerKey",
        "class ScreenLayerOrder",
        "struct ScreenLayerSlot",
        "class ScreenLayers")
    $productionSourceFiles = @(Get-ChildItem (Join-Path $repositoryRoot "src") -Recurse -Filter *.cs)

    foreach ($sourceFile in $productionSourceFiles) {
        $relativePath = Get-RepositoryRelativePath $sourceFile.FullName
        if ($relativePath.StartsWith($screenRoot)) {
            continue
        }

        $source = Get-Content -Raw $sourceFile.FullName
        foreach ($declaration in $genericScreenDeclarations) {
            if ($source.Contains($declaration, [StringComparison]::Ordinal)) {
                $violations.Add("$relativePath declares generic Machina-owned screen type '$declaration' outside $screenRoot.")
            }
        }
    }

    $aurelianScreenDirectory = Join-Path $repositoryRoot "src/Aurelian/Aurelian.Core/Presentation/Screens"
    $aurelianScreenSourceFiles = @()
    if (Test-Path $aurelianScreenDirectory -PathType Container) {
        $aurelianScreenSourceFiles = @(Get-ChildItem $aurelianScreenDirectory -Filter *.cs)
    }

    if ($aurelianScreenSourceFiles.Count -gt 0) {
        $violations.Add("Aurelian.Core.Presentation.Screens production namespace remains after screen ownership migration.")
    }

    $integrationSolution = Get-Content -Raw (Join-Path $repositoryRoot "JointTaskForce.Integration.slnx")
    $crossSystemSamples = @(
        "samples/Machina.UI/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj",
        "samples/Integrations/Machina.Presenter.Sample/Machina.Presenter.Sample.csproj",
        "samples/Integrations/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj")

    foreach ($sampleProject in $crossSystemSamples) {
        if (-not $integrationSolution.Contains($sampleProject, [StringComparison]::Ordinal)) {
            $violations.Add("Cross-system rasterizing sample $sampleProject must be a JointTaskForce.Integration solution member.")
        }
    }

    foreach ($solutionName in @("Machina.UI.slnx", "Machina.UI.Slow.slnx")) {
        $solutionText = Get-Content -Raw (Join-Path $repositoryRoot $solutionName)
        foreach ($sampleProject in $crossSystemSamples) {
            if ($solutionText.Contains($sampleProject, [StringComparison]::Ordinal)) {
                $violations.Add("Machina-only solution $solutionName includes cross-system rasterizing sample $sampleProject.")
            }
        }
    }

    $retiredVisibleTriangleRoot = Join-Path $repositoryRoot "samples/Aurelian/Aurelian.VisibleTriangle"
    if (Test-Path $retiredVisibleTriangleRoot -PathType Container) {
        $violations.Add("Cross-system visible-triangle sample must not remain under samples/Aurelian after M4b.")
    }

    $retiredPresenterRoot = Join-Path $repositoryRoot "samples/Machina.UI/Machina.Presenter.Sample"
    if (Test-Path $retiredPresenterRoot -PathType Container) {
        $violations.Add("Cross-system presenter sample must not remain under samples/Machina.UI after M4c.")
    }
}

function Add-CanonicalInputRoutingViolations {
    $retiredInputTokens = @(
        "PresenterInputEvent",
        "PresenterInputKind",
        "PresenterInputButton",
        "PresenterInputPoint",
        "PresenterKeyboardInput",
        "PresenterKeyModifiers")
    $sourceRoots = @(
        (Join-Path $repositoryRoot "src"),
        (Join-Path $repositoryRoot "samples"),
        (Join-Path $repositoryRoot "tests"))

    foreach ($sourceRoot in $sourceRoots) {
        foreach ($sourceFile in @(Get-ChildItem $sourceRoot -Recurse -Filter *.cs)) {
            $source = Get-Content -Raw $sourceFile.FullName
            foreach ($token in $retiredInputTokens) {
                if ($source.Contains($token, [StringComparison]::Ordinal)) {
                    $violations.Add("$(Get-RepositoryRelativePath $sourceFile.FullName) retains retired presenter compatibility input token '$token'.")
                }
            }
        }
    }

    $frontendRouter = Join-Path $repositoryRoot "src/Machina.UI/Machina.Presentation/Input/MachinaFrontendInputRouter.cs"
    if (-not (Test-Path $frontendRouter -PathType Leaf)) {
        $violations.Add("Machina frontend input router is missing; UiInputBatch must remain the canonical lifecycle routing input.")
    } else {
        $routerSource = Get-Content -Raw $frontendRouter
        if (-not $routerSource.Contains("Route(UiInputBatch inputBatch)", [StringComparison]::Ordinal)) {
            $violations.Add("Machina frontend input router must expose UiInputBatch as its canonical routing input.")
        }
    }

    $presenterRouter = Join-Path $repositoryRoot "samples/Integrations/Machina.Presenter.Sample/PresenterUiInputRouting.cs"
    if (-not (Test-Path $presenterRouter -PathType Leaf)) {
        $violations.Add("Integration-owned presenter UiInputBatch router is missing.")
    } else {
        $routerSource = Get-Content -Raw $presenterRouter
        if (-not $routerSource.Contains("UiInputBatch inputBatch", [StringComparison]::Ordinal)) {
            $violations.Add("Presenter routing must consume UiInputBatch directly.")
        }
    }

    $translatorSource = Get-Content -Raw (Join-Path $repositoryRoot "src/Integrations/Aurelian.Machina/AurelianHostInputTranslator.cs")
    if ($translatorSource.Contains("case UiCloseRequested", [StringComparison]::Ordinal)) {
        $violations.Add("Aurelian lifecycle translation must not consume UiCloseRequested directly; close must cross MachinaFrontendCloseRequested.")
    }

    if ($translatorSource.Contains("UiInputBatch", [StringComparison]::Ordinal)) {
        $violations.Add("Aurelian.Machina lifecycle translation must consume Machina frontend messages, not UiInputBatch.")
    }

    $hostCollector = Join-Path $repositoryRoot "samples/Integrations/Aurelian.VisibleTriangle/VisibleTriangleHostInputCollector.cs"
    if (-not (Test-Path $hostCollector -PathType Leaf)) {
        $violations.Add("The integration-owned visible-triangle host collector is missing.")
    } else {
        $collectorSource = Get-Content -Raw $hostCollector
        foreach ($requiredMember in @("void Record(UiInputEvent inputEvent)", "UiInputBatch Publish()", "pendingEvents.Clear()")) {
            if (-not $collectorSource.Contains($requiredMember, [StringComparison]::Ordinal)) {
                $violations.Add("The integration-owned host collector must retain ordered publish-and-drain behavior: missing '$requiredMember'.")
            }
        }
    }

    $frameLoop = Join-Path $repositoryRoot "src/Aurelian/Aurelian.Core/Engine/Frames/AurelianFrameLoop.cs"
    if (-not (Test-Path $frameLoop -PathType Leaf)) {
        $violations.Add("Aurelian frame loop is missing the explicit close acceptance boundary.")
    } else {
        $frameLoopSource = Get-Content -Raw $frameLoop
        foreach ($requiredToken in @("input.CloseRequest", "AcceptCloseRequest", "AurelianFrameLoopStopReason.CloseRequested")) {
            if (-not $frameLoopSource.Contains($requiredToken, [StringComparison]::Ordinal)) {
                $violations.Add("Aurelian frame loop must accept typed close requests before another frame: missing '$requiredToken'.")
            }
        }
    }

    $visibleHost = Join-Path $repositoryRoot "samples/Integrations/Aurelian.VisibleTriangle/SilkNetFrameInputProvider.cs"
    if (Test-Path $visibleHost -PathType Leaf) {
        $visibleHostSource = Get-Content -Raw $visibleHost
        foreach ($requiredToken in @("inputCollector.Publish()", "MachinaFrontendInputRouter.Route(inputBatch)", "AurelianHostInputTranslator.Translate(")) {
            if (-not $visibleHostSource.Contains($requiredToken, [StringComparison]::Ordinal)) {
                $violations.Add("Visible-triangle host must retain the canonical typed close path: missing '$requiredToken'.")
            }
        }
    }
}

function Add-MachinaPresentationOnlyViolations {
    $deletedProjectFiles = @(Get-ChildItem (Join-Path $repositoryRoot "src") -Recurse -Filter "Machina.Renderer.Raster*.csproj")
    foreach ($projectFile in $deletedProjectFiles) {
        $violations.Add("Legacy Machina renderer project still exists: $(Get-RepositoryRelativePath $projectFile.FullName).")
    }

    $pipelineProject = Join-Path $repositoryRoot "src/Machina.UI/Machina.Pipeline/Machina.Pipeline.csproj"
    [xml]$pipeline = Get-Content -Raw $pipelineProject
    foreach ($reference in @($pipeline.Project.ItemGroup.ProjectReference)) {
        $include = [string]$reference.Include
        if ($include.Contains("Dominatus", [StringComparison]::OrdinalIgnoreCase) -or
            $include.Contains("Renderer", [StringComparison]::OrdinalIgnoreCase)) {
            $violations.Add("Machina.Pipeline must be presentation-only; found project reference $include.")
        }
    }

    $machinaProjects = @(Get-ChildItem (Join-Path $repositoryRoot "src/Machina.UI") -Recurse -Filter *.csproj)
    foreach ($projectFile in $machinaProjects) {
        $projectPath = Get-RepositoryRelativePath $projectFile.FullName
        Add-TextDependencyViolations $projectPath @(
            "Aurelian.",
            "Machina.Renderer.Raster",
            "RasterFrame",
            "RasterSurface",
            "RasterPpmEncoder",
            "LegacyMachinaRenderCommandAdapter")
    }

    foreach ($solutionFile in @(Get-ChildItem $repositoryRoot -Filter *.slnx)) {
        $solutionText = Get-Content -Raw $solutionFile.FullName
        if ($solutionText.Contains("Machina.Renderer.Raster", [StringComparison]::Ordinal)) {
            $violations.Add("$(Get-RepositoryRelativePath $solutionFile.FullName) retains a deleted Machina renderer project path.")
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

    if ($projectPath -eq "src/Integrations/Aurelian.Machina/Aurelian.Machina.csproj") {
        $allowedBridgeReferences = @(
            "src/Machina.UI/Machina.Presentation/Machina.Presentation.csproj",
            "src/Machina.UI/Machina.Runtime/Machina.Runtime.csproj",
            "src/Aurelian/Aurelian.Core/Aurelian.Core.csproj",
            "src/Aurelian/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj")
    }

    foreach ($packageReference in @($project.Project.ItemGroup.PackageReference)) {
        $package = [string]$packageReference.Include
        if ([string]::IsNullOrWhiteSpace($package)) {
            continue
        }

        if ($package -like "Dominatus.*") {
            $approvedDominatusOwners = @(
                "src/Aurelian/Aurelian.Runtime/Aurelian.Runtime.csproj",
                "src/Integrations/Machina.Dominatus/Machina.Dominatus.csproj")

            if ($projectPath -notin $approvedDominatusOwners) {
                $violations.Add("$projectPath references Dominatus package $package outside an approved owner.")
            }
        }

        if ($sourceSubsystem -eq "Aurelian" -and $package -like "Machina*") {
            $violations.Add("$projectPath references prohibited Machina package $package.")
        }

        if ($projectPath -eq "src/Integrations/Aurelian.Machina/Aurelian.Machina.csproj") {
            $violations.Add("Aurelian.Machina must not reference package $package.")
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

        if ($projectPath -eq "src/Aurelian/Aurelian.Rendering.Raster/Aurelian.Rendering.Raster.csproj") {
            $violations.Add("Aurelian.Rendering.Raster must not reference package $package.")
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

        if ($projectPath -eq "src/Aurelian/Aurelian.Rendering.Raster/Aurelian.Rendering.Raster.csproj" -and
            $targetRelativePath -ne "src/Aurelian/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj") {
            $violations.Add("Aurelian.Rendering.Raster may reference only Aurelian.Rendering.Contracts; found $targetRelativePath.")
        }

        if ($projectPath -eq "src/Integrations/Aurelian.Machina/Aurelian.Machina.csproj" -and
            $targetRelativePath -notin $allowedBridgeReferences) {
            $violations.Add("Aurelian.Machina may reference only Machina.Presentation and Aurelian.Rendering.Contracts; found $targetRelativePath.")
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
Add-TextDependencyViolations "src/Aurelian/Aurelian.Rendering.Raster/Aurelian.Rendering.Raster.csproj" @(
    "Machina",
    "Dominatus",
    "Silk.NET",
    "Vulkan",
    "Windowing",
    "Aurelian.Core",
    "Aurelian.Runtime",
    "Aurelian.Graphics")
Add-TextDependencyViolations "src/Machina.UI/Machina.Runtime/Machina.Runtime.csproj" @(
    "Avalonia.Input",
    "Silk.NET",
    "Aurelian.")
Add-TextDependencyViolations "src/Aurelian/Aurelian.Core/Aurelian.Core.csproj" @(
    "Machina.",
    "Avalonia",
    "Silk.NET",
    "Windowing")
Add-TextDependencyViolations "src/Integrations/Aurelian.Machina/Aurelian.Machina.csproj" @(
    "Machina.Dominatus",
    "Machina.Pipeline",
    "Machina.Renderer.Raster",
    "Aurelian.Rendering.Raster",
    "Aurelian.Runtime",
    "Aurelian.Graphics",
    "Dominatus",
    "Silk.NET",
    "Windowing",
    "samples",
    "Tests")
Add-ProjectGraphCycleViolations
Add-SolutionTopologyViolations
Add-IntegrationOwnershipViolations
Add-MachinaPresentationOnlyViolations
Add-ScreenOwnershipViolations
Add-CanonicalInputRoutingViolations
Add-DominatusOwnershipViolations

if ($violations.Count -gt 0) {
    Write-Error ("Dependency boundary validation failed:`n- " + ($violations -join "`n- "))
    exit 1
}

Write-Output "Dependency boundary validation passed for $($projects.Count) production projects."
Write-Output "No dependency-boundary exceptions are permitted."
