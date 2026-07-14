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

$tsonSourceRoot = Join-Path $root "src/Copeland/Copeland.TS/Tson"
$tsonFixtureRoot = Join-Path $root "tests/Copeland/Copeland.TS.Tests/Tson"
Require-Condition (Test-Path -LiteralPath $tsonSourceRoot -PathType Container) "The colocated TSON semantic-pass source root is missing."
Require-Condition (Test-Path -LiteralPath $tsonFixtureRoot -PathType Container) "The TSON fixture root is missing."
Require-Condition (Test-Path -LiteralPath (Join-Path $tsonFixtureRoot "Valid") -PathType Container) "TSON/Valid fixture ownership is missing."
Require-Condition (Test-Path -LiteralPath (Join-Path $tsonFixtureRoot "Invalid") -PathType Container) "TSON/Invalid fixture ownership is missing."

$tsonObjectFixtures = @(Get-ChildItem -LiteralPath $tsonFixtureRoot -Recurse -Filter *.obj.ts -File)
$tsonCanonicalFixtures = @(Get-ChildItem -LiteralPath $tsonFixtureRoot -Recurse -Filter *.tson -File)
Require-Condition ($tsonObjectFixtures.Count -gt 0) "TSON fixtures must include the .obj.ts authoring profile."
Require-Condition ($tsonCanonicalFixtures.Count -gt 0) "TSON fixtures must include the canonical .tson profile."

$tsonSources = @(Get-ChildItem -LiteralPath $tsonSourceRoot -Recurse -Filter *.cs -File)
$duplicateFrontendDeclarations = $tsonSources | Select-String -Pattern 'class\s+Tson(Lexer|Parser)\b|enum\s+TsonSyntaxKind\b|enum\s+TsonTokenKind\b'
Require-Condition ($null -eq $duplicateFrontendDeclarations) "TSON must not define a second lexer, parser, token-kind table, or syntax-kind hierarchy."

$readerSource = Get-Content -Raw -LiteralPath (Join-Path $tsonSourceRoot "TsonDocumentReader.cs")
Require-Condition ($readerSource.IndexOf('SyntaxTree.Parse(source)', [System.StringComparison]::Ordinal) -ge 0) "Both TSON profiles must invoke the production SyntaxTree.Parse entry point."
Require-Condition (-not ($readerSource.IndexOf('new Parser(', [System.StringComparison]::Ordinal) -ge 0)) "TSON must enter parsing only through the production SyntaxTree facade."

$forbiddenTsonDependencies = $tsonSources | Select-String -Pattern 'Copeland\.TS\.Backend|Copeland\.Cli|Machina|Aurelian|Dominatus|Microsoft\.CodeAnalysis|System\.Reflection|System\.Text\.Json|Newtonsoft'
Require-Condition ($null -eq $forbiddenTsonDependencies) "TSON contains a prohibited backend, CLI, product, Roslyn, reflection, or serializer dependency."

$forbiddenTsonVariants = $tsonSources | Select-String -Pattern 'class\s+Tson(Result|Json)\b|record\s+Tson(Result|Json)\b'
Require-Condition ($null -eq $forbiddenTsonVariants) "TSON must not implement Result or JSON variants."
Require-Condition ($readerSource.IndexOf('ArrayLiteralExpressionSyntax', [System.StringComparison]::Ordinal) -ge 0) "TSON arrays must reuse the production ArrayLiteralExpressionSyntax."
Require-Condition (-not ($readerSource.IndexOf('$array(', [System.StringComparison]::Ordinal) -ge 0)) "TSON must not define a parallel array grammar."
Require-Condition ($readerSource.IndexOf('TableDeclarationSyntax', [System.StringComparison]::Ordinal) -ge 0) "TSON table projection must reuse production TableDeclarationSyntax."
Require-Condition (-not ($readerSource.IndexOf('TsonTableParser', [System.StringComparison]::Ordinal) -ge 0)) "TSON tables must not define a parallel parser."

$tsonAssetFixtureRoot = Join-Path $root "tests/Copeland/Copeland.TS.Tests/TsonAssets"
Require-Condition (Test-Path -LiteralPath (Join-Path $tsonAssetFixtureRoot "Valid") -PathType Container) "TSON asset Valid fixture ownership is missing."
Require-Condition (Test-Path -LiteralPath (Join-Path $tsonAssetFixtureRoot "Invalid") -PathType Container) "TSON asset Invalid fixture ownership is missing."
$tsonAssetSources = @(Get-ChildItem -LiteralPath $tsonAssetFixtureRoot -Recurse -Filter *.asset-*.ts -File)
$tsonAssetObjectFiles = @(Get-ChildItem -LiteralPath $tsonAssetFixtureRoot -Recurse -Filter *.obj.ts -File)
$tsonAssetCanonicalFiles = @(Get-ChildItem -LiteralPath $tsonAssetFixtureRoot -Recurse -Filter *.tson -File)
Require-Condition ($tsonAssetSources.Count -ge 3) "TSON asset fixtures must own valid and invalid source cases."
Require-Condition ($tsonAssetObjectFiles.Count -gt 0) "TSON asset fixtures must own an Object TypeScript asset."
Require-Condition ($tsonAssetCanonicalFiles.Count -gt 0) "TSON asset fixtures must own a canonical TSON asset."

$tsonTableAssetFixtureRoot = Join-Path $root "tests/Copeland/Copeland.TS.Tests/TsonTableAssets"
Require-Condition (Test-Path -LiteralPath (Join-Path $tsonTableAssetFixtureRoot "Valid") -PathType Container) "TSON table-asset Valid fixture ownership is missing."
Require-Condition (Test-Path -LiteralPath (Join-Path $tsonTableAssetFixtureRoot "Invalid") -PathType Container) "TSON table-asset Invalid fixture ownership is missing."
Require-Condition (Test-Path -LiteralPath (Join-Path $tsonTableAssetFixtureRoot "Corpus") -PathType Container) "TSON table-asset corpus ownership is missing."

$parserSource = Get-Content -Raw -LiteralPath (Join-Path $root "src/Copeland/Copeland.TS/Syntax/Parser.cs")
$syntaxFactsSource = Get-Content -Raw -LiteralPath (Join-Path $root "src/Copeland/Copeland.TS/Syntax/SyntaxFacts.cs")
Require-Condition ($parserSource.IndexOf('TableAssetClauseSyntax', [System.StringComparison]::Ordinal) -ge 0) "Declaration-owned table assets must extend the production table parser."
Require-Condition (-not ($syntaxFactsSource.IndexOf('["from"]', [System.StringComparison]::Ordinal) -ge 0)) "The table asset 'from' token must remain contextual."

$mirSources = @(Get-ChildItem -LiteralPath (Join-Path $root "src/Copeland/Copeland.TS.Mir") -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
$forbiddenMirTson = $mirSources | Select-String -Pattern '\b(TsonValue|TsonDocument|TsonCatalog|TsonDocumentReader|TsonCanonicalPrinter)\b|Copeland\.TS\.Tson'
Require-Condition ($null -eq $forbiddenMirTson) "Cope MIR must not contain or reference compiler-host TSON types."

$mirText = ($mirSources | Get-Content -Raw) -join "`n"
Require-Condition ($mirText.IndexOf('class MirTsonEncodingPlan', [System.StringComparison]::Ordinal) -ge 0) "Runtime TSON encoding plans must be owned by Copeland.TS.Mir."
Require-Condition ($mirText.IndexOf('record MirTsonEncodeExpression', [System.StringComparison]::Ordinal) -ge 0) "Runtime TSON encoding requires a dedicated MIR expression."
Require-Condition ($mirText.IndexOf('ValidateTsonEncodingModel', [System.StringComparison]::Ordinal) -ge 0) "Runtime TSON encoding plans require shared MIR validation."
Require-Condition ($mirText.IndexOf('MirTsonArrayPlan', [System.StringComparison]::Ordinal) -ge 0) "ARRAY-M1 runtime array encoding plans must be owned by Copeland.TS.Mir."
Require-Condition ($mirText.IndexOf('MaximumArrayLength', [System.StringComparison]::Ordinal) -ge 0) "ARRAY-M1 runtime array encoding plans require a shared array limit."
Require-Condition ($mirText.IndexOf('MirTableArrayConstant', [System.StringComparison]::Ordinal) -ge 0) "TABLE-M1 array-valued cells require a closed MIR table-array constant."
Require-Condition ($mirText.IndexOf('class MirTsonTablePlan', [System.StringComparison]::Ordinal) -ge 0) "TABLE-M2 runtime table encoding plans must be owned by Copeland.TS.Mir."

$backendSources = @(
    Get-ChildItem -LiteralPath (Join-Path $root "src/Copeland/Copeland.TS.Backend.CSharp") -Recurse -Filter *.cs -File
    Get-ChildItem -LiteralPath (Join-Path $root "src/Copeland/Copeland.TS.Backend.JavaScript") -Recurse -Filter *.cs -File
) | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
$forbiddenBackendTson = $backendSources | Select-String -Pattern '\b(TsonValue|TsonDocument|TsonCatalog|TsonDocumentReader)\b|Copeland\.TS\.Tson'
Require-Condition ($null -eq $forbiddenBackendTson) "Backends must not reference compiler-host TSON types."
$forbiddenBackendAssets = $backendSources | Select-String -Pattern '\bICopelandAssetSource\b|\bCopelandAssetResolver\b|\btsonAsset\b'
Require-Condition ($null -eq $forbiddenBackendAssets) "Backends must not reference compiler-host asset abstractions or table asset syntax."
$javaScriptBackendText = Get-Content -Raw -LiteralPath (Join-Path $root "src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs")
Require-Condition ($javaScriptBackendText.IndexOf('MirArrayExpression array => EmitArrayExpression', [System.StringComparison]::Ordinal) -ge 0) "JavaScript arrays must be realized through the ordinary backend path."
Require-Condition ($javaScriptBackendText.IndexOf('Array.isArray(array)', [System.StringComparison]::Ordinal) -ge 0) "JavaScript TSON array encoding must validate ordinary array carriers."

$javaScriptBackendRoot = Join-Path $root "src/Copeland/Copeland.TS.Backend.JavaScript"
$javaScriptEmissionModel = Get-Content -Raw -LiteralPath (Join-Path $javaScriptBackendRoot "JavaScriptEmissionModel.cs")
Require-Condition ($javaScriptEmissionModel.IndexOf('record struct JavaScriptBindingId', [System.StringComparison]::Ordinal) -ge 0) "Generated JavaScript bindings must retain backend-local typed identities."
Require-Condition ($javaScriptEmissionModel.IndexOf('class JavaScriptNameAllocator', [System.StringComparison]::Ordinal) -ge 0) "Generated JavaScript names must originate from the scoped backend allocator."
Require-Condition ($javaScriptEmissionModel.IndexOf('class JavaScriptTokenWriter', [System.StringComparison]::Ordinal) -ge 0) "JavaScript emission must retain its backend-local token writer."
Require-Condition ($javaScriptBackendText.IndexOf('new JavaScriptNameAllocator', [System.StringComparison]::Ordinal) -ge 0) "The Diagnostic backend must use the backend-local JavaScript name allocator."
Require-Condition ($javaScriptBackendText.IndexOf('Dictionary<EnumInfo, JavaScriptBindingReference>', [System.StringComparison]::Ordinal) -ge 0) "Generated-name catalogs must retain typed binding references instead of string names."
$javaScriptWriterText = Get-Content -Raw -LiteralPath (Join-Path $javaScriptBackendRoot "JavaScriptTextWriter.cs")
Require-Condition ($javaScriptWriterText.IndexOf('BindingPart(JavaScriptBindingReference', [System.StringComparison]::Ordinal) -ge 0) "Diagnostic writer binding references must be structured events."
Require-Condition ($javaScriptWriterText.IndexOf('document.Reference(line.Scope', [System.StringComparison]::Ordinal) -ge 0) "Diagnostic writer binding references must validate lexical scope."
$javaScriptProfilesText = Get-Content -Raw -LiteralPath (Join-Path $javaScriptBackendRoot "JavaScriptEmissionProfile.cs")
$javaScriptSymbolicText = Get-Content -Raw -LiteralPath (Join-Path $javaScriptBackendRoot "SymbolicJavaScriptVocabulary.cs")

$javaScriptProductionSources = @(Get-ChildItem -LiteralPath $javaScriptBackendRoot -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
$forbiddenJavaScriptTooling = $javaScriptProductionSources | Select-String -Pattern 'Terser|esbuild|SWC|Babel|Uglify|sourceMappingURL|\.map\b'
Require-Condition ($null -eq $forbiddenJavaScriptTooling) "The JavaScript backend must not add an external minifier, parser, or source-map output path."
Require-Condition ($javaScriptProfilesText.IndexOf('enum JavaScriptEmissionProfile', [System.StringComparison]::Ordinal) -ge 0) "JavaScript emission profiles must remain an explicit backend contract."
Require-Condition ($javaScriptProfilesText.IndexOf('Symbolic', [System.StringComparison]::Ordinal) -ge 0) "The executable Symbolic JavaScript profile must remain available."
Require-Condition ($javaScriptSymbolicText.IndexOf('CTS-JS-EMIT-M1', [System.StringComparison]::Ordinal) -ge 0) "Symbolic JavaScript vocabulary must remain versioned and closed."
$forbiddenReleaseProfile = $javaScriptProductionSources | Select-String -Pattern 'JavaScriptEmissionProfile\.Release|enum JavaScriptEmissionProfile[^{]*{[^}]*Release|Release allocator'
Require-Condition ($null -eq $forbiddenReleaseProfile) "Release JavaScript emission remains outside M1 production code."

$forbiddenRuntimeEncodingApis = $backendSources | Select-String -Pattern 'System\.Text\.Json|JSON\.stringify|System\.Reflection|System\.IO\.File|File\.(Read|Write)|\breflection\b|\bdynamic\b'
Require-Condition ($null -eq $forbiddenRuntimeEncodingApis) "Generated backends must not add JSON, reflection, dynamic, or runtime filesystem dependencies for TSON encoding."

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
    Where-Object {
        -not $_.FullName.StartsWith($javaScriptFixtureRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        -not $_.FullName.StartsWith((Join-Path $tsonAssetFixtureRoot "Corpus"), [System.StringComparison]::OrdinalIgnoreCase) -and
        -not $_.FullName.StartsWith((Join-Path $tsonTableAssetFixtureRoot "Corpus"), [System.StringComparison]::OrdinalIgnoreCase) -and
        -not $_.FullName.StartsWith(
            (Join-Path $root "tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus"),
            [System.StringComparison]::OrdinalIgnoreCase)
    }
Require-Condition ($misownedJavaScriptArtifacts.Count -eq 0) "Generated JavaScript fixtures must be owned by Copeland.TS.Backend.JavaScript.Tests."

$symbolicArtifacts = Get-ChildItem -LiteralPath (Join-Path $root "tests/Copeland") -Recurse -Filter *.sym.js -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
foreach ($artifact in $symbolicArtifacts) {
    $sourcePath = [System.IO.Path]::ChangeExtension($artifact.FullName.Substring(0, $artifact.FullName.Length - ".sym.js".Length), ".ts")
    Require-Condition (Test-Path -LiteralPath $sourcePath -PathType Leaf) "Symbolic JavaScript artifact has no sibling source fixture: $($artifact.FullName)"
}
$misownedSymbolicArtifacts = $symbolicArtifacts |
    Where-Object {
        -not $_.FullName.StartsWith($javaScriptFixtureRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        -not $_.FullName.StartsWith((Join-Path $tsonAssetFixtureRoot "Corpus"), [System.StringComparison]::OrdinalIgnoreCase) -and
        -not $_.FullName.StartsWith((Join-Path $tsonTableAssetFixtureRoot "Corpus"), [System.StringComparison]::OrdinalIgnoreCase) -and
        -not $_.FullName.StartsWith(
            (Join-Path $root "tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus"),
            [System.StringComparison]::OrdinalIgnoreCase)
    }
Require-Condition ($misownedSymbolicArtifacts.Count -eq 0) "Generated Symbolic JavaScript fixtures must be owned by the JavaScript or TSON corpus roots."

Write-Output "Copeland TS topology validation passed."
