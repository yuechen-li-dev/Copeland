[CmdletBinding()]
param(
    [string]$Version = "0.1.0-preview.1",
    [string]$Configuration = "Release",
    [string]$ReleaseRoot,
    [switch]$SkipBuildAndTests,
    [switch]$SkipPackageSmoke
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ReleaseRoot = if ($ReleaseRoot) {
    $ReleaseRoot
}
else {
    Join-Path $repositoryRoot "artifacts\releases\$Version"
}
$releaseRoot = [IO.Path]::GetFullPath($ReleaseRoot)
$expectedReleaseRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\releases\$Version"))
if ($releaseRoot -ne $expectedReleaseRoot) {
    throw "ReleaseRoot must be the versioned repository path '$expectedReleaseRoot'."
}

$nugetRoot = Join-Path $releaseRoot "nuget"
$npmRoot = Join-Path $releaseRoot "npm"
$vsCodeRoot = Join-Path $releaseRoot "vscode"
$templateRoot = Join-Path $releaseRoot "templates"
$stagingRoot = Join-Path $releaseRoot ".staging"

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Reset-GeneratedDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset generated directory outside ${releaseRoot}: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $fullPath | Out-Null
}

function Get-ZipEntries {
    param([Parameter(Mandatory)][string]$Path)

    Add-Type -AssemblyName System.IO.Compression
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        return @($archive.Entries | ForEach-Object { $_.FullName })
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-NoForbiddenArchiveNames {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string[]]$Entries
    )

    $forbidden = $Entries | Where-Object {
        $_ -match '(^|/)(\.git|obj|TestResults|\.vscode-test)(/|$)' -or
        $_ -match '\.(user|suo|cache)$'
    }
    if ($forbidden) {
        throw "$Path contains forbidden package entries:`n$($forbidden -join [Environment]::NewLine)"
    }
}

& (Join-Path $PSScriptRoot "Validate-CopelandPreviewVersion.ps1") -ExpectedVersion $Version
& (Join-Path $PSScriptRoot "Test-CopelandReleaseClosure.ps1") -RepositoryRoot $repositoryRoot

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
foreach ($directory in @($nugetRoot, $npmRoot, $vsCodeRoot, $templateRoot, $stagingRoot)) {
    Reset-GeneratedDirectory $directory
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$npm = (Get-Command npm -ErrorAction Stop).Source
$git = (Get-Command git -ErrorAction Stop).Source
$pwsh = (Get-Command pwsh -ErrorAction Stop).Source
$solution = Join-Path $repositoryRoot "Copeland.Release.slnx"
if (-not $SkipBuildAndTests) {
    Write-Output "Restoring release product closure: $solution"
    Invoke-Checked $dotnet restore $solution
    Write-Output "Building release product closure: $solution"
    Invoke-Checked $dotnet build $solution --configuration $Configuration --no-restore
    # Several CLI integration tests intentionally launch nested builds with a
    # tight timeout. Serialize test projects so CI load cannot starve them.
    Write-Output "Testing release-authoritative projects: $solution"
    Invoke-Checked $dotnet test $solution --configuration $Configuration --no-build --maxcpucount:1
}

$toolProject = Join-Path $repositoryRoot "src\Copeland\Copeland.Cli\Copeland.Cli.csproj"
$sdkProject = Join-Path $repositoryRoot "src\Copeland\Copeland.TS.MSBuild\Copeland.TS.MSBuild.csproj"
$templatesProject = Join-Path $repositoryRoot "src\Copeland\Copeland.TS.Templates\Copeland.TS.Templates.csproj"
Invoke-Checked $dotnet pack $toolProject --configuration $Configuration --output $nugetRoot
Invoke-Checked $dotnet pack $sdkProject --configuration $Configuration --output $nugetRoot
Invoke-Checked $dotnet pack $templatesProject --configuration $Configuration --output $nugetRoot

$toolPackage = Join-Path $nugetRoot "Copeland.TS.Tool.$Version.nupkg"
$sdkPackage = Join-Path $nugetRoot "Copeland.TS.Sdk.$Version.nupkg"
$templatesPackage = Join-Path $nugetRoot "Copeland.TS.Templates.$Version.nupkg"
foreach ($package in @($toolPackage, $sdkPackage, $templatesPackage)) {
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "Expected NuGet package was not produced: $package"
    }
    $entries = Get-ZipEntries $package
    Assert-NoForbiddenArchiveNames $package $entries
    $entries | Set-Content -LiteralPath (Join-Path $stagingRoot ((Split-Path $package -Leaf) + ".contents.txt")) -Encoding utf8
}

$sdkEntries = Get-ZipEntries $sdkPackage
foreach ($requiredEntry in @(
    "build/Copeland.TS.Sdk.props",
    "build/Copeland.TS.Sdk.targets",
    "buildTransitive/Copeland.TS.Sdk.props",
    "buildTransitive/Copeland.TS.Sdk.targets",
    "tools/net10.0/Copeland.TS.MSBuild.dll"
)) {
    if ($sdkEntries -notcontains $requiredEntry) {
        throw "Copeland.TS.Sdk is missing required package entry '$requiredEntry'."
    }
}

$templateEntries = Get-ZipEntries $templatesPackage
foreach ($requiredEntry in @(
    "content/copeland-console/.template.config/template.json",
    "content/copeland-library/.template.config/template.json",
    "content/copeland-react/.template.config/template.json",
    "content/copeland-workspace/.template.config/template.json"
)) {
    if ($templateEntries -notcontains $requiredEntry) {
        throw "Copeland.TS.Templates is missing required package entry '$requiredEntry'."
    }
}

$npmSource = Join-Path $repositoryRoot "src\Copeland\Copeland.TS.Npm"
$npmStage = Join-Path $stagingRoot "npm-package"
Reset-GeneratedDirectory $npmStage
Copy-Item -LiteralPath (Join-Path $npmSource "package.json") -Destination $npmStage
Copy-Item -LiteralPath (Join-Path $npmSource "README.md") -Destination $npmStage
Copy-Item -LiteralPath (Join-Path $npmSource "THIRD_PARTY_NOTICES.md") -Destination $npmStage
Copy-Item -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Destination (Join-Path $npmStage "LICENSE")
Copy-Item -LiteralPath (Join-Path $npmSource "bin") -Destination $npmStage -Recurse
$npmPayload = Join-Path $npmStage "payload"
Invoke-Checked $dotnet publish $toolProject --configuration $Configuration --no-restore --self-contained false /p:UseAppHost=false --output $npmPayload
Get-ChildItem -LiteralPath $npmPayload -File |
    Where-Object { $_.Extension -in @(".pdb", ".xml") } |
    Remove-Item -Force

Push-Location $npmStage
try {
    & $npm pack --dry-run --json | Set-Content -LiteralPath (Join-Path $stagingRoot "npm-pack-dry-run.json") -Encoding utf8
    if ($LASTEXITCODE -ne 0) {
        throw "npm pack --dry-run failed with exit code $LASTEXITCODE."
    }
    & $npm pack --pack-destination $npmRoot --json | Set-Content -LiteralPath (Join-Path $stagingRoot "npm-pack.json") -Encoding utf8
    if ($LASTEXITCODE -ne 0) {
        throw "npm pack failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$npmPackage = Join-Path $npmRoot "copeland-tscl-$Version.tgz"
if (-not (Test-Path -LiteralPath $npmPackage -PathType Leaf)) {
    throw "Expected npm package was not produced: $npmPackage"
}
$npmEntries = & tar -tf $npmPackage
if ($LASTEXITCODE -ne 0) {
    throw "Could not inspect npm tarball $npmPackage."
}
Assert-NoForbiddenArchiveNames $npmPackage $npmEntries
if ($npmEntries | Where-Object { $_ -match '(^|/)node_modules(/|$)' }) {
    throw "$npmPackage unexpectedly contains node_modules."
}
$npmEntries | Set-Content -LiteralPath (Join-Path $stagingRoot "npm-contents.txt") -Encoding utf8

$vsCodeProject = Join-Path $repositoryRoot "src\Copeland\Copeland.TS.VSCode"
Push-Location $vsCodeProject
try {
    Invoke-Checked $npm ci
    Invoke-Checked $npm test
    Invoke-Checked $npm run package
}
finally {
    Pop-Location
}
$builtVsix = Join-Path $vsCodeProject "dist\copeland-ts-$Version.vsix"
$vsixPackage = Join-Path $vsCodeRoot "copeland-ts-$Version.vsix"
Copy-Item -LiteralPath $builtVsix -Destination $vsixPackage -Force
$vsixEntries = Get-ZipEntries $vsixPackage
Assert-NoForbiddenArchiveNames $vsixPackage $vsixEntries
$vsixEntries | Set-Content -LiteralPath (Join-Path $stagingRoot "vsix-contents.txt") -Encoding utf8

$templateArtifact = Join-Path $templateRoot "BootstrapTemplate.tsx"
Copy-Item -LiteralPath (Join-Path $repositoryRoot "samples\copeland-ts\templates\BootstrapTemplate.tsx") -Destination $templateArtifact -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot "docs\Copeland\releases\0.1.0-preview.1.md") -Destination (Join-Path $releaseRoot "RELEASE_NOTES.md") -Force

if (-not $SkipPackageSmoke) {
    Invoke-Checked $pwsh -NoProfile -File (Join-Path $PSScriptRoot "Test-CopelandPreviewPackages.ps1") -ReleaseRoot $releaseRoot -Version $Version
}

$artifactDefinitions = @(
    @{ Path = $toolPackage; PackageId = "Copeland.TS.Tool"; Target = "NuGet.org"; Runtime = ".NET 10; any tool-supported OS" },
    @{ Path = $sdkPackage; PackageId = "Copeland.TS.Sdk"; Target = "NuGet.org"; Runtime = ".NET 10 MSBuild" },
    @{ Path = $templatesPackage; PackageId = "Copeland.TS.Templates"; Target = "NuGet.org"; Runtime = ".NET 10 SDK template engine" },
    @{ Path = $npmPackage; PackageId = "@copeland/tscl"; Target = "npm (preview tag)"; Runtime = "Windows x64; Node.js 20+; .NET 10" },
    @{ Path = $vsixPackage; PackageId = "copeland.copeland-ts"; Target = "GitHub Release"; Runtime = "VS Code 1.99+; Copeland tool $Version" },
    @{ Path = $templateArtifact; PackageId = "BootstrapTemplate"; Target = "GitHub Release"; Runtime = "Copeland TS $Version" }
)

$manifestArtifacts = foreach ($definition in $artifactDefinitions) {
    $item = Get-Item -LiteralPath $definition.Path
    [ordered]@{
        name = $item.Name
        version = $Version
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        size = $item.Length
        packageId = $definition.PackageId
        publicationTarget = $definition.Target
        requiredRuntimePlatform = $definition.Runtime
        validationStatus = if ($SkipPackageSmoke) { "packed; smoke not run" } else { "package-only smoke passed" }
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    product = "Copeland TS"
    version = $Version
    validatedScope = "Preview release product closure validated"
    releaseSolution = "Copeland.Release.slnx"
    excludedPrerequisiteBoundSamples = @(
        "tsxml-react-m0 browser host",
        "standalone-web-m0 TSPack host"
    )
    status = if ($SkipPackageSmoke) { "packed" } else { "ready to publish" }
    published = $false
    publiclyVerified = $false
    artifacts = $manifestArtifacts
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot "MANIFEST.json") -Encoding utf8

$checksumLines = $manifestArtifacts | ForEach-Object { "$($_.sha256)  $($_.name)" }
$checksumLines | Set-Content -LiteralPath (Join-Path $releaseRoot "checksums.txt") -Encoding ascii

Remove-Item -LiteralPath $stagingRoot -Recurse -Force

& (Join-Path $PSScriptRoot "Validate-CopelandPreviewVersion.ps1") -ExpectedVersion $Version
& $git -C $repositoryRoot diff --check
if ($LASTEXITCODE -ne 0) {
    throw "git diff --check failed."
}

Write-Output "Copeland TS $Version release artifacts are ready at $releaseRoot"
