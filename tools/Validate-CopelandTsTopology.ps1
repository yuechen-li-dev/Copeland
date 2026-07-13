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
[xml]$mirProjectXml = Get-Content -LiteralPath $mirProject
$mirPackageReferences = @($mirProjectXml.SelectNodes('//PackageReference'))
Require-Condition ($mirPackageReferences.Count -eq 0) "Copeland.TS.Mir must remain BCL-only and must not reference NuGet packages."

$frontendProject = Join-Path $root "src/Copeland/Copeland.TS/Copeland.TS.csproj"
$csharpBackendProject = Join-Path $root "src/Copeland/Copeland.TS.Backend.CSharp/Copeland.TS.Backend.CSharp.csproj"
$javaScriptBackendProject = Join-Path $root "src/Copeland/Copeland.TS.Backend.JavaScript/Copeland.TS.Backend.JavaScript.csproj"
$cliProject = Join-Path $root "src/Copeland/Copeland.Cli/Copeland.Cli.csproj"
Require-Condition ($projectReferences[$frontendProject] -contains $mirProject) "Copeland.TS must reference Copeland.TS.Mir."
Require-Condition ($projectReferences[$csharpBackendProject] -contains $mirProject) "Copeland.TS.Backend.CSharp must reference Copeland.TS.Mir."
Require-Condition ($projectReferences[$javaScriptBackendProject] -contains $mirProject) "Copeland.TS.Backend.JavaScript must reference Copeland.TS.Mir."
Require-Condition ($projectReferences[$frontendProject].Count -eq 1) "Copeland.TS may depend only on Copeland.TS.Mir."
Require-Condition ($projectReferences[$csharpBackendProject].Count -eq 1) "Copeland.TS.Backend.CSharp may depend only on Copeland.TS.Mir."
Require-Condition ($projectReferences[$javaScriptBackendProject].Count -eq 1) "Copeland.TS.Backend.JavaScript may depend only on Copeland.TS.Mir."
Require-Condition ($projectReferences[$csharpBackendProject] -notcontains $frontendProject) "The C# backend must not reference the frontend."
Require-Condition ($projectReferences[$javaScriptBackendProject] -notcontains $frontendProject) "The JavaScript backend must not reference the frontend."
Require-Condition ($projectReferences[$frontendProject] -notcontains $csharpBackendProject) "Copeland.TS must not reference a concrete backend."
Require-Condition ($projectReferences[$frontendProject] -notcontains $javaScriptBackendProject) "Copeland.TS must not reference a concrete backend."
Require-Condition ($projectReferences[$cliProject] -contains $frontendProject) "Copeland.Cli must compose Copeland.TS."
Require-Condition ($projectReferences[$cliProject] -contains $csharpBackendProject) "Copeland.Cli must compose Copeland.TS.Backend.CSharp."
Require-Condition ($projectReferences[$cliProject] -contains $javaScriptBackendProject) "Copeland.Cli must compose Copeland.TS.Backend.JavaScript."
Require-Condition ($projectReferences[$cliProject] -contains $mirProject) "Copeland.Cli must explicitly compose Copeland.TS.Mir."

$markdownProject = Join-Path $root "src/Copeland/Copeland.Markdown/Copeland.Markdown.csproj"
$vdMirProject = Join-Path $root "src/Aurelian/Aurelian.Shaders/Aurelian.Shaders.csproj"
foreach ($pair in @(
    @($mirProject, $markdownProject, "Cope MIR and DocumentMir must remain independent."),
    @($mirProject, $vdMirProject, "Cope MIR and VD-MIR must remain independent."),
    @($markdownProject, $vdMirProject, "DocumentMir and VD-MIR must remain independent."))) {
    $left = $pair[0]
    $right = $pair[1]
    $message = $pair[2]
    Require-Condition (-not ($projectReferences[$left] -contains $right)) $message
    Require-Condition (-not ($projectReferences[$right] -contains $left)) $message
}


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
    Where-Object { $_.Name -match 'cope-test' }
Require-Condition ($copeTestFiles.Count -eq 0) "An abandoned Cope Test fixture convention remains."

$languageRoot = Join-Path $root "tests/Copeland/Copeland.TS.Tests/Language"
Require-Condition (Test-Path -LiteralPath $languageRoot -PathType Container) "Copeland TS Language fixture root is missing."

$languageAreas = @{
    Valid = @("conditions", "declarations", "functions", "arrays", "fallibility", "tagged-data")
    Invalid = @("conditions", "declarations", "dynamic-types", "absence", "coercions", "functions", "fallibility", "tagged-data")
}

foreach ($category in $languageAreas.Keys) {
    $categoryRoot = Join-Path $languageRoot $category
    Require-Condition (Test-Path -LiteralPath $categoryRoot -PathType Container) "Copeland TS Language/$category fixture directory is missing."

    foreach ($area in $languageAreas[$category]) {
        $areaRoot = Join-Path $categoryRoot $area
        Require-Condition (Test-Path -LiteralPath $areaRoot -PathType Container) "Copeland TS Language/$category/$area fixture directory is missing."
    }
}

$languageFiles = @(Get-ChildItem -LiteralPath $languageRoot -Recurse -File)
foreach ($fixture in $languageFiles) {
    $relativePath = $fixture.FullName.Substring($languageRoot.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    Require-Condition (-not ($fixture.Name -match '\.cope$|\.g\.cs$|\.g\.js$')) "Language contains a generated or MIR artifact: $relativePath"

    $isValidFixture = $relativePath -match '^Valid[\\/].*\.cl-valid\.ts$'
    $isInvalidFixture = $relativePath -match '^Invalid[\\/].*\.cl-invalid\.ts$'
    Require-Condition ($isValidFixture -or $isInvalidFixture) "Language fixture does not follow the required suffix convention: $relativePath"
}

$validLanguageFixtures = @($languageFiles | Where-Object { $_.FullName -match '[\\/]Valid[\\/].*\.cl-valid\.ts$' })
$invalidLanguageFixtures = @($languageFiles | Where-Object { $_.FullName -match '[\\/]Invalid[\\/].*\.cl-invalid\.ts$' })
Require-Condition ($validLanguageFixtures.Count -gt 0) "Copeland TS Language/Valid must contain at least one .cl-valid.ts fixture."
Require-Condition ($invalidLanguageFixtures.Count -gt 0) "Copeland TS Language/Invalid must contain at least one .cl-invalid.ts fixture."

$forbiddenAbstractions = Get-ChildItem -LiteralPath (Join-Path $root "src/Copeland") -Recurse -Filter *.cs -File |
    Select-String -Pattern 'IBackend|ICompilerBackend|IIntermediateRepresentation|ICompilerPass'
Require-Condition ($null -eq $forbiddenAbstractions) "A forbidden universal compiler abstraction was introduced."

$copeFixtures = Get-ChildItem -LiteralPath (Join-Path $root "tests/Copeland/Copeland.TS.Tests/TestData/Corpus") -Recurse -Filter *.cope -File
foreach ($fixture in $copeFixtures) {
    Require-Condition ($fixture.Directory.Name -match 'mir') ".cope fixture is not owned by a MIR corpus case: $($fixture.FullName)"
}

$javaScriptFixtureRoot = Join-Path $root "tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus"
Require-Condition (Test-Path -LiteralPath $javaScriptFixtureRoot -PathType Container) "JavaScript backend corpus root is missing."
$javaScriptArtifacts = @(Get-ChildItem -LiteralPath $javaScriptFixtureRoot -Recurse -Filter *.g.js -File)
Require-Condition ($javaScriptArtifacts.Count -gt 0) "JavaScript backend corpus must contain at least one .g.js artifact."
foreach ($artifact in $javaScriptArtifacts) {
    $sourcePath = [System.IO.Path]::ChangeExtension($artifact.FullName.Substring(0, $artifact.FullName.Length - ".g.js".Length), ".ts")
    Require-Condition (Test-Path -LiteralPath $sourcePath -PathType Leaf) "JavaScript backend artifact has no sibling source fixture: $($artifact.FullName)"
}

$misownedJavaScriptArtifacts = Get-ChildItem -LiteralPath (Join-Path $root "tests/Copeland") -Recurse -Filter *.g.js -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Where-Object { -not $_.FullName.StartsWith($javaScriptFixtureRoot, [System.StringComparison]::OrdinalIgnoreCase) }
Require-Condition ($misownedJavaScriptArtifacts.Count -eq 0) "Generated JavaScript fixtures must be owned by Copeland.TS.Backend.JavaScript.Tests."

Write-Output "Copeland TS topology validation passed."
