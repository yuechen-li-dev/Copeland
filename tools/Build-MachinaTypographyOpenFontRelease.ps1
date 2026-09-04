[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [string]$ReleaseRoot
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a valid NuGet semantic version."
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$expectedReleaseRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\releases\machina-typography-openfont\$Version"))
$releaseRoot = if ($ReleaseRoot) {
    [IO.Path]::GetFullPath($ReleaseRoot)
}
else {
    $expectedReleaseRoot
}

if ($releaseRoot -ne $expectedReleaseRoot) {
    throw "ReleaseRoot must be the versioned repository path '$expectedReleaseRoot'."
}

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
    if (-not $fullPath.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        $fullPath -ne $releaseRoot) {
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

Reset-GeneratedDirectory $releaseRoot
$nugetRoot = Join-Path $releaseRoot "nuget"
$stagingRoot = Join-Path $releaseRoot ".staging"
New-Item -ItemType Directory -Force -Path $nugetRoot, $stagingRoot | Out-Null

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$project = Join-Path $repositoryRoot "src\ThirdParty\Machina.Typography.OpenFont\Machina.Typography.OpenFont.csproj"
$testProject = Join-Path $repositoryRoot "tests\Machina.UI\Machina.Typography.OpenFont.Tests\Machina.Typography.OpenFont.Tests.csproj"

Invoke-Checked $dotnet test $testProject --configuration $Configuration --maxcpucount:1
Invoke-Checked $dotnet pack $project `
    --configuration $Configuration `
    --output $nugetRoot `
    /p:PackageVersion=$Version `
    /p:ContinuousIntegrationBuild=true

$package = Join-Path $nugetRoot "Machina.Typography.OpenFont.$Version.nupkg"
$symbolsPackage = Join-Path $nugetRoot "Machina.Typography.OpenFont.$Version.snupkg"
foreach ($path in @($package, $symbolsPackage)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Expected package was not produced: $path"
    }
}

$entries = Get-ZipEntries $package
$requiredEntries = @(
    "LICENSE.md",
    "PATCHES.md",
    "README.md",
    "UPSTREAM.md",
    "lib/netstandard2.0/Machina.Typography.OpenFont.dll"
)
foreach ($requiredEntry in $requiredEntries) {
    if ($entries -notcontains $requiredEntry) {
        throw "Package is missing required entry '$requiredEntry'."
    }
}

$forbiddenEntries = $entries | Where-Object {
    $_ -match '(^|/)(\.git|bin|obj|TestResults)(/|$)' -or
    $_ -match '\.(user|suo|cache)$'
}
if ($forbiddenEntries) {
    throw "Package contains forbidden entries:`n$($forbiddenEntries -join [Environment]::NewLine)"
}
$entries | Set-Content -LiteralPath (Join-Path $stagingRoot "package-contents.txt") -Encoding utf8

$smokeRoot = Join-Path $stagingRoot "consumer-smoke"
New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null
$escapedNugetRoot = [Security.SecurityElement]::Escape($nugetRoot)
$smokeProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <RestoreSources>$escapedNugetRoot;https://api.nuget.org/v3/index.json</RestoreSources>
    <RestorePackagesPath>$(Join-Path $stagingRoot "packages")</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Machina.Typography.OpenFont" Version="$Version" />
  </ItemGroup>
</Project>
"@
$smokeProject | Set-Content -LiteralPath (Join-Path $smokeRoot "PackageSmoke.csproj") -Encoding utf8

$smokeProgram = @'
using Typography.OpenFont;

if (args.Length != 1)
{
    throw new ArgumentException("Expected a font path.");
}

using FileStream stream = File.OpenRead(args[0]);
Typeface typeface = new OpenFontReader().Read(stream);
ushort glyphIndex = typeface.GetGlyphIndex(' ');
Glyph glyph = typeface.GetGlyph(glyphIndex);
ushort advance = typeface.GetAdvanceWidthFromGlyphIndex(glyph.GlyphIndex);

if (glyphIndex != 556 || glyph.GlyphIndex != 556 || advance != 229)
{
    throw new InvalidOperationException(
        $"Package smoke failed: mapped={glyphIndex}, returned={glyph.GlyphIndex}, advance={advance}.");
}

Console.WriteLine(
    $"Package smoke passed: glyph={glyphIndex}, advance={advance}, assembly={typeof(Typeface).Assembly.GetName().Name}.");
'@
$smokeProgram | Set-Content -LiteralPath (Join-Path $smokeRoot "Program.cs") -Encoding utf8

$fontPath = Join-Path $repositoryRoot "tests\Machina.UI\Machina.Fonts.Tests\Fixtures\Fonts\CrimsonText-Regular.ttf"
Invoke-Checked $dotnet run `
    --project (Join-Path $smokeRoot "PackageSmoke.csproj") `
    --configuration $Configuration `
    -- $fontPath

$artifacts = foreach ($path in @($package, $symbolsPackage)) {
    $item = Get-Item -LiteralPath $path
    [ordered]@{
        name = $item.Name
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        size = $item.Length
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    packageId = "Machina.Typography.OpenFont"
    version = $Version
    upstreamCommit = "5877180c7c5271091379a0eaf9f03ab6ebd256b3"
    licenseFile = "LICENSE.md"
    validationStatus = "package tests and clean package-consumer smoke passed"
    publicationTarget = "NuGet.org trusted publishing"
    published = $false
    artifacts = $artifacts
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot "MANIFEST.json") -Encoding utf8

$checksumLines = foreach ($artifact in $artifacts) {
    "$($artifact.sha256)  $($artifact.name)"
}
$checksumLines | Set-Content -LiteralPath (Join-Path $releaseRoot "checksums.txt") -Encoding ascii

Remove-Item -LiteralPath $stagingRoot -Recurse -Force
Write-Output "Machina.Typography.OpenFont $Version is ready to publish from $releaseRoot"
